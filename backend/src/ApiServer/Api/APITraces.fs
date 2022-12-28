/// API endpoints to fetch Traces
module ApiServer.Traces

open System.Threading.Tasks
open FSharp.Control.Tasks
open Microsoft.AspNetCore.Http

open Prelude
open Tablecloth
open Http

module Traces = LibBackend.Traces
module Canvas = LibBackend.Canvas
module Telemetry = LibService.Telemetry

module AT = LibExecution.AnalysisTypes
module CTApi = ClientTypes.Api
module CT2Runtime = ClientTypes2ExecutionTypes.Runtime

module TraceDataV1 =
  module Types = CTApi.Traces.GetTraceDataV1

  /// API endpoint to fetch data for a specific Trace
  ///
  /// Data returned includes input, timestamp, and results
  let getTraceData (ctx : HttpContext) : Task<Option<Types.Response.T>> =
    task {
      use t = startTimer "read-api" ctx
      let canvasInfo = loadCanvasInfo ctx
      let! p = ctx.ReadVanillaJsonAsync<Types.Request>()
      Telemetry.addTags [ "tlid", p.tlid; "traceID", p.traceID ]

      let traceID = AT.TraceID.fromUUID p.traceID

      t.next "load-canvas"
      let! c = Canvas.loadTLIDs canvasInfo [ p.tlid ]


      // CLEANUP: we dont need the handlers or functions at all here, just for the sample
      // values which we could do on the client instead
      t.next "load-trace"
      let handler = c.handlers |> Map.get p.tlid

      let! rootTLID = LibBackend.TraceCloudStorage.rootTLIDFor c.meta.id traceID

      let! trace =
        match rootTLID with
        | Some rootTLID ->
          LibBackend.TraceCloudStorage.getTraceData
            c.meta.id
            rootTLID
            (AT.TraceID.fromUUID p.traceID)
          |> Task.map Some
        | None ->
          match handler with
          | Some h -> Traces.handlerTrace c.meta.id traceID h |> Task.map Some
          | None ->
            match c.userFunctions |> Map.get p.tlid with
            | Some u -> Traces.userfnTrace c.meta.id traceID u |> Task.map Some
            | None -> Task.FromResult None

      t.next "write-api"
      let (trace : Option<Types.Response.Trace>) =
        match trace with
        | Some (traceID, (traceData : AT.TraceData)) ->
          Some(
            AT.TraceID.toUUID traceID,
            { input =
                List.map
                  (fun (s, dv) -> (s, CT2Runtime.Dval.toCT dv))
                  traceData.input
              timestamp = AT.TraceID.toTimestamp traceID
              functionResults =
                List.map
                  (fun (r1, r2, r3, r4, dv) ->
                    (r1, r2, r3, r4, CT2Runtime.Dval.toCT dv))
                  traceData.function_results }
          )
        | None -> None

      return Option.map (fun t -> { trace = t }) trace
    }

module AllTraces =
  /// API endpoint to fetch a list of Traces for a Toplevel
  ///
  /// Only returns metadata - does not include inputs/outputs
  let fetchAll (ctx : HttpContext) : Task<CTApi.Traces.GetAllTraces.Response> =
    task {
      use t = startTimer "read-api" ctx
      let canvasInfo = loadCanvasInfo ctx

      // CLEANUP we only need the HTTP handler paths here, so we can remove the loadAll
      // CLEANUP don't load traces for deleted handlers
      t.next "load-canvas"
      let! (c : Canvas.T) = Canvas.loadAll canvasInfo

      t.next "fetch-handler-traces"
      let! hTraces =
        c.handlers
        |> Map.values
        |> List.map (fun h ->
          Traces.traceIDsForHandler c h
          |> Task.map (List.map (fun traceid -> (h.tlid, traceid))))
        |> Task.flatten
        |> Task.map List.concat


      t.next "fetch-userfn-traces"
      let! ufTraces =
        c.userFunctions
        |> Map.values
        |> List.map (fun uf ->
          Traces.traceIDsForUserFn c.meta.id uf.tlid
          |> Task.map (List.map (fun traceID -> (uf.tlid, traceID))))
        |> Task.flatten
        |> Task.map List.concat

      t.next "fetch-storage-traces"
      let tlids = Map.keys c.userFunctions @ Map.keys c.handlers
      let! storageTraces =
        LibBackend.TraceCloudStorage.listMostRecentTraceIDsForTLIDs c.meta.id tlids

      t.next "write-api"
      let traces =
        (storageTraces @ hTraces @ ufTraces)
        |> List.map (fun (k, traceID) -> (k, AT.TraceID.toUUID traceID))
      return { traces = traces }
    }
