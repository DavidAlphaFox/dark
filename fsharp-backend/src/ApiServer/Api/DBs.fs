module ApiServer.DBs

// DB-related API endpoints

open Microsoft.AspNetCore.Http
open Giraffe
open Giraffe.EndpointRouting

open System.Threading.Tasks
open FSharp.Control.Tasks
open FSharpPlus
open Prelude
open Tablecloth

open Npgsql.FSharp.Tasks
open Npgsql
open LibBackend.Db

module PT = LibBackend.ProgramTypes
module OT = LibBackend.OCamlInterop.OCamlTypes
module ORT = LibBackend.OCamlInterop.OCamlTypes.RuntimeT
module AT = LibExecution.AnalysisTypes
module Convert = LibBackend.OCamlInterop.Convert

module Account = LibBackend.Account
module Stats = LibBackend.Stats
module Traces = LibBackend.Traces
module Auth = LibBackend.Authorization
module Canvas = LibBackend.Canvas
module Config = LibBackend.Config
module RT = LibExecution.RuntimeTypes
module SA = LibBackend.StaticAssets
module Session = LibBackend.Session
module TFA = LibBackend.TraceFunctionArguments
module TFR = LibBackend.TraceFunctionResults
module TI = LibBackend.TraceInputs

module Unlocked =
  type T = { unlocked_dbs : tlid list }

  let getUnlockedDBs (ctx : HttpContext) : Task<T> =
    task {
      let t = Middleware.startTimer ctx
      let canvasInfo = Middleware.loadCanvasInfo ctx
      t "loadCanvasInfo"

      let! unlocked = LibBackend.UserDB.unlocked canvasInfo.owner canvasInfo.id
      t "getUnlocked"
      return { unlocked_dbs = unlocked }
    }

module DBStats =
  type Params = { tlids : tlid list }
  type Stat = { count : int; example : Option<ORT.dval * string> }
  type T = Map<tlid, Stat>

  let getStats (ctx : HttpContext) : Task<T> =
    task {
      let t = Middleware.startTimer ctx
      let canvasInfo = Middleware.loadCanvasInfo ctx
      let! args = ctx.BindModelAsync<Params>()
      t "readApiTLIDs"

      let! c = Canvas.loadAllDBs canvasInfo |> Task.map Result.unwrapUnsafe
      t "loadSavedOps"

      let! result = Stats.dbStats c args.tlids

      // CLEANUP, this is shimming an RT.Dval into an ORT.dval. Nightmare.
      let (result : T) =
        Map.map
          (fun (s : Stats.DBStat) ->
            { count = s.count
              example =
                Option.map (fun (dv, s) -> (Convert.rt2ocamlDval dv, s)) s.example })
          result

      t "analyse-db-stats"

      return result
    }

let endpoints : Endpoint list =
  let h = Middleware.apiHandler

  [ POST [ routef "/api/%s/get_unlocked_dbs" (h Unlocked.getUnlockedDBs Auth.Read)
           routef "/api/%s/get_db_stats" (h DBStats.getStats Auth.Read) ] ]
