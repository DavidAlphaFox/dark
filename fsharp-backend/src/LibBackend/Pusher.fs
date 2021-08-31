module LibBackend.Pusher

open System.Threading.Tasks
open FSharp.Control.Tasks

type ExecutionID = LibService.Telemetry.ExecutionID

open Prelude
open Tablecloth

module AT = LibExecution.AnalysisTypes

// PusherClient has own internal serializer which matches this interface.
type Serializer() =
  interface PusherServer.ISerializeObjectsToJson with
    member this.Serialize(x : obj) : string = Json.OCamlCompatible.serialize x


let pusherClient : Lazy<PusherServer.Pusher> =
  lazy
    (let options = PusherServer.PusherOptions()
     options.Cluster <- Config.pusherCluster
     options.set_JsonSerializer (Serializer())

     PusherServer.Pusher(
       Config.pusherID,
       Config.pusherKey,
       Config.pusherSecret,
       options
     ))


// Send an event to pusher. Note: this is fired in the backgroup, and does not
// take any time from the current thread. You cannot wait for it, by design.
let push
  (executionID : ExecutionID)
  (canvasID : CanvasID)
  (eventName : string)
  (payload : 'x)
  : unit =
  let client = Lazy.force pusherClient

  // TODO: handle messages over 10k
  // TODO: make channels private and end-to-end encrypted in order to add public canvases

  let (_ : Task<unit>) =
    task {
      try
        let channel = $"canvas_{canvasID}"
        // FSTODO add executionID

        let! (_ : PusherServer.ITriggerResult) =
          client.TriggerAsync(channel, eventName, payload)

        return ()
      with
      | e ->
        // swallow this error
        print $"Error Sending push to Pusher {eventName}: {canvasID}: {e.ToString()}"

        LibService.Rollbar.send
          executionID
          [ "canvasID", string canvasID; "event", eventName; "context", "pusher" ]
          e

      return ()
    }
  // do not wait for the push task to finish, just fire and forget
  ()



let pushNewTraceID
  (executionID : ExecutionID)
  (canvasID : CanvasID)
  (traceID : AT.TraceID)
  (tlids : tlid list)
  : unit =
  push executionID canvasID "new_trace" (traceID, tlids)


let pushNew404
  (executionID : ExecutionID)
  (canvasID : CanvasID)
  (f404 : TraceInputs.F404)
  =
  push executionID canvasID "new_404" f404


let pushNewStaticDeploy
  (executionID : ExecutionID)
  (canvasID : CanvasID)
  (asset : StaticAssets.StaticDeploy)
  =
  push executionID canvasID "new_static_deploy" asset


// For exposure as a DarkInternal function
let pushAddOpEvent
  (executionID : ExecutionID)
  (canvasID : CanvasID)
  (event : Op.AddOpEvent)
  =
  push executionID canvasID "add_op" event

let pushWorkerStates
  (executionID : ExecutionID)
  (canvasID : CanvasID)
  (ws : EventQueue.WorkerStates.T)
  : unit =
  push executionID canvasID "worker_state" ws

type JsConfig = { enabled : bool; key : string; cluster : string }

let jsConfigString =
  // CLEANUP use JSON serialization
  $"{{enabled: true, key: '{Config.pusherKey}', cluster: '{Config.pusherCluster}'}}"
