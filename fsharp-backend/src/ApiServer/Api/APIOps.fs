module ApiServer.AddOps

// Functions and API endpoints for the API

open Microsoft.AspNetCore.Http
open Giraffe
open Giraffe.EndpointRouting

open System.Threading.Tasks
open FSharp.Control.Tasks
open FSharpPlus
open Prelude
open Tablecloth

module C = LibBackend.Canvas
module Serialize = LibBackend.Serialize
module Op = LibBackend.Op
module PT = LibExecution.ProgramTypes
module OT = LibExecution.OCamlTypes
module ORT = LibExecution.OCamlTypes.RuntimeT
module AT = LibExecution.AnalysisTypes
module Convert = LibExecution.OCamlTypes.Convert


// Toplevel deletion:
// * The server announces that a toplevel is deleted by it appearing in
// * deleted_toplevels. The server announces it is no longer deleted by it
// * appearing in toplevels again.

// A subset of responses to be merged in
type T = Op.AddOpEvent

type Params = Op.AddOpParams

let empty : Op.AddOpResult =
  { toplevels = []
    deleted_toplevels = []
    user_functions = []
    deleted_user_functions = []
    user_tipes = []
    deleted_user_tipes = [] }

let causesAnyChanges (ops : PT.Oplist) : bool = List.any Op.hasEffect ops

let addOp (ctx : HttpContext) : Task<T> =
  task {
    let t = Middleware.startTimer ctx
    let canvasInfo = Middleware.loadCanvasInfo ctx
    let executionID = Middleware.loadExecutionID ctx

    let! p = ctx.BindModelAsync<Params>()
    let canvasID = canvasInfo.id

    let! isLatest = Serialize.isLatestOpRequest p.clientOpCtrId p.opCtr canvasInfo.id

    let newOps = Convert.ocamlOplist2PT p.ops
    let newOps = if isLatest then newOps else Op.filterOpsReceivedOutOfOrder newOps
    t "read-api"

    let! dbTLIDs =
      match Op.requiredContextToValidateOplist newOps with
      | Op.NoContext -> Task.FromResult []
      // NOTE: Because we run canvas-wide validation logic, it's important that
      // we load _at least_ the context (ie. datastores, functions, types, etc)
      // and not just the tlids in the API payload.
      | Op.AllDatastores -> Serialize.fetchTLIDsForAllDBs canvasInfo.id

    let allTLIDs = (List.map Op.tlidOf newOps) @ dbTLIDs
    // We're going to save this, so we need all the ops
    let! oldOps = C.loadOplists C.IncludeDeletedToplevels canvasInfo.id allTLIDs
    let oldOps = oldOps |> List.map Tuple2.second |> List.concat

    let c = C.fromOplist canvasInfo oldOps newOps |> Result.unwrapUnsafe

    t "2-load-saved-ops"

    let toplevels = C.toplevels c
    let deletedToplevels = C.deletedToplevels c

    let (tls, fns, types) = Convert.pt2ocamlToplevels toplevels
    let (dTLs, dFns, dTypes) = Convert.pt2ocamlToplevels deletedToplevels

    let result : Op.AddOpResult =
      { toplevels = tls
        deleted_toplevels = dTLs
        user_functions = fns
        deleted_user_functions = dFns
        user_tipes = types
        deleted_user_tipes = dTypes }

    t "3-to-frontend"

    // work out the result before we save it, in case it has a
    // stackoverflow or other crashing bug
    if causesAnyChanges newOps then
      do!
        (oldOps @ newOps)
        |> Op.oplist2TLIDOplists
        |> List.map
             (fun (tlid, oplists) ->
               let (tl, deleted) =
                 match Map.get tlid toplevels with
                 | Some tl -> tl, C.NotDeleted
                 | None ->
                   match Map.get tlid deletedToplevels with
                   | Some tl -> tl, C.Deleted
                   | None ->
                     failwith "couldn't find the TL we supposedly just looked up"

               (tlid, oplists, tl, deleted))
        |> C.saveTLIDs canvasInfo


    t "4-save-to-disk"

    let event =
      // To make this work with prodclone, we might want to have it specify
      // more ... else people's prodclones will stomp on each other ...
      if causesAnyChanges newOps then
        let event : Op.AddOpEvent = { result = result; ``params`` = p }
        LibBackend.Pusher.pushAddOpEvent executionID canvasID event
        event
      else
        { result = empty; ``params`` = p }

    t "5-send-ops-to-pusher"

    // NB: I believe we only send one op at a time, but the type is op list
    newOps
    // MoveTL and TLSavepoint make for noisy data, so exclude it from heapio
    |> List.filter
         (function
         | PT.MoveTL _
         | PT.TLSavepoint _ -> false
         | _ -> true)
    |> List.iter
         (fun op ->
           LibBackend.HeapAnalytics.track
             executionID
             canvasInfo.id
             canvasInfo.name
             canvasInfo.owner
             (Op.eventNameOfOp op)
             Map.empty)

    t "6-send-event-to-heapio"

    // FSTODO
    // Span.set_attr parent "op_ctr" (Int p.opCtr)

    return event
  }
