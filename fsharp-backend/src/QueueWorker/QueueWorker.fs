module QueueWorker

open System.Threading.Tasks
open FSharp.Control.Tasks

open Prelude
open Prelude.Tablecloth
open Tablecloth
open LibBackend.Db

module PT = LibExecution.ProgramTypes
module RT = LibExecution.RuntimeTypes
module EQ = LibBackend.EventQueue
module TI = LibBackend.TraceInputs
module Execution = LibExecution.Execution
module Pusher = LibBackend.Pusher
module RealExecution = LibRealExecution.RealExecution
module Canvas = LibBackend.Canvas

module Telemetry = LibService.Telemetry

let dequeueAndProcess () : Task<Result<Option<RT.Dval>, exn>> =
  task {
    use _span = Telemetry.child "dequeue_and_process" []
    let executionID = Telemetry.executionID ()
    return!
      Sql.withTransaction (fun () ->
        task {
          let! event =
            try
              EQ.dequeue () |> Task.map Ok
            with
            | e ->
              // exception occurred while dequeuing, no item to put back
              Telemetry.addEvent "Exception while dequeuing" []
              Task.FromResult(Error e)

          match event with
          | Ok (None) ->
            Telemetry.addTag "event_queue.no_events" true
            return Ok None
          | Ok (Some event) ->
            let! canvas =
              task {
                // Span creation might belong inside
                // Canvas.loadForEvent, but then so would the
                // error handling ... this may want a refactor
                use span = Telemetry.child "Canvas.load_for_event_from_cache" []
                try
                  let! c = Canvas.loadForEvent event
                  let c =
                    c |> Result.mapError (String.concat ", ") |> Result.unwrapUnsafe
                  Telemetry.addTag "load_event_succeeded" true
                  return Ok c
                with
                | e ->
                  // exception occurred when processing an item, so put it back as an error
                  do! EQ.putBack event EQ.Err
                  // CLEANUP why have these attributes a different name
                  Telemetry.addTag "event.load_success" false
                  return Error e
              }

            match canvas with
            | Ok c ->
              let host = c.meta.name
              let traceID = System.Guid.NewGuid()
              let canvasID = c.meta.id
              let desc = EQ.toEventDesc event

              Telemetry.addTags [ "canvas", host
                                  "trace_id", traceID
                                  "canvas_id", canvasID
                                  "module", event.space
                                  "handler_name", event.name
                                  "method", event.modifier
                                  "retries", event.retries ]

              try
                let! eventTimestamp = TI.storeEvent canvasID traceID desc event.value

                let h =
                  c.handlers
                  |> Map.values
                  |> List.filter (fun h -> Some desc = h.spec.toEventDesc ())
                  |> List.head


                Telemetry.addTags [ "host", host
                                    "event", desc
                                    "event_id", event.id ]

                match h with
                | None ->
                  // If an event gets put in the queue and there's no handler for
                  // it, they're probably emiting to a handler they haven't created
                  // yet. This creates a number of problems. Firstly, the event
                  // will sit in the queue and rattle around forever, which is bad
                  // operationally. However, it will also constantly run while the
                  // user is editing code, until something finally works. This is
                  // annoying, but also unnecessary - so long as they have the
                  // trace they can use it to build. So just drop it immediately.
                  Telemetry.addTag "delay" event.delay
                  let space, name, modifier = desc
                  let f404 = (space, name, modifier, eventTimestamp, traceID)
                  Pusher.pushNew404 executionID canvasID f404
                  do! EQ.putBack event EQ.Missing
                  return Ok None
                | Some h ->
                  let! (state, touchedTLIDs) =
                    RealExecution.createState
                      executionID
                      traceID
                      h.tlid
                      (Canvas.toProgram c)

                  Telemetry.addTag "handler_id" h.tlid
                  let symtable = Map.ofList [ ("event", event.value) ]

                  let! result =
                    Execution.executeHandler state symtable (h.ast.toRuntimeType ())

                  Pusher.pushNewTraceID
                    executionID
                    canvasID
                    traceID
                    (h.tlid :: HashSet.toList touchedTLIDs)

                  let resultType =
                    match result with
                    | RT.DResult (Ok _) -> "ResOk"
                    | RT.DResult (Error _) -> "ResError"
                    | RT.DOption (Some _) -> "OptJust"
                    | RT.DOption None -> "OptNothing"
                    | _ -> (RT.Dval.toType result).toOldString ()


                  Telemetry.addTags [ "result_tipe", resultType
                                      "event.execution_success", true ]

                  do! EQ.finish event
                  return Ok(Some result)
              with
              | e ->
                // exception occurred when processing an item, so put it back as an error
                Telemetry.addTag "event.execution_success" false

                try
                  do! EQ.putBack event EQ.Err
                with
                | e -> Telemetry.addTag "error.msg" e

                return Error e
            | Error e -> return Error e
          | Error e -> return Error e
        })
  }

let shutdown = ref false

let run () : Task<unit> =
  task {
    Telemetry.createRoot "QueueWorker.run"
    while not shutdown.Value do
      // Comment out just in case for now
      // let! result = dequeueAndProcess ()
      let result = Ok None
      match result with
      | Ok None -> do! Task.Delay 1000
      | Ok (Some _) -> return ()
      | Error (e) ->
        LibService.Rollbar.sendException
          "Unhandled exception bubbled to queue worker"
          (Telemetry.executionID ())
          []
          e
  }


[<EntryPoint>]
let main _ : int =
  try
    print "Starting QueueWorker"
    LibService.Init.init "QueueWorker"
    Telemetry.Console.loadTelemetry "QueueWorker"
    LibExecution.Init.init "QueueWorker"
    LibExecutionStdLib.Init.init "QueueWorker"
    LibBackend.Init.init "QueueWorker"
    BackendOnlyStdLib.Init.init "QueueWorker"
    LibRealExecution.Init.init "QueueWorker"

    // we need to stop taking things if we're told to stop by k8s
    LibService.Kubernetes.runKubernetesServer
      "QueueWorker"
      LibService.Config.queueWorkerKubernetesPort
      (fun () ->
        Telemetry.addEvent "shutting down" []
        shutdown.Value <- true)
    |> ignore<Task>

    if false then
      // LibBackend.Config.triggerQueueWorkers then
      (run ()).Result
    else
      Telemetry.addEvent "Pointing at prodclone; will not dequeue" []
    0

  with
  | e ->
    LibService.Rollbar.lastDitchBlocking
      "Error running CronChecker"
      (Prelude.ExecutionID "cronchecker")
      []
      e
    (-1)
