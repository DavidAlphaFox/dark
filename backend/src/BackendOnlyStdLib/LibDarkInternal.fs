/// StdLib functions for building Dark functionality via Dark canvases
module BackendOnlyStdLib.LibDarkInternal

open System.Threading.Tasks

open Npgsql.FSharp
open LibBackend.Db

open Prelude

open LibExecution.RuntimeTypes

module PT = LibExecution.ProgramTypes
module DvalReprDeveloper = LibExecution.DvalReprDeveloper
module Errors = LibExecution.Errors
module Telemetry = LibService.Telemetry

open LibBackend
module SchedulingRules = LibBackend.QueueSchedulingRules

let fn = FQFnName.stdlibFnName
let typ = FQTypeName.stdlibTypeName

let incorrectArgs = LibExecution.Errors.incorrectArgs

let varA = TVariable "a"
let varB = TVariable "b"


// only accessible to the LibBackend.Config.allowedDarkInternalCanvasID canvas
let internalFn (f : BuiltInFnSig) : BuiltInFnSig =
  (fun (state, typeArgs, args) ->
    uply {
      if state.program.internalFnsAllowed then
        return! f (state, typeArgs, args)
      else
        return
          Exception.raiseInternal
            "internal function attempted to be used in another canvas"
            [ "canavasId", state.program.canvasID ]
    })

let modifySchedule (fn : CanvasID -> string -> Task<unit>) =
  internalFn (function
    | _, _, [ DUuid canvasID; DString handlerName ] ->
      uply {
        do! fn canvasID handlerName
        let! s = SchedulingRules.getWorkerSchedules canvasID
        Pusher.push
          ClientTypes2BackendTypes.Pusher.eventSerializer
          canvasID
          (Pusher.UpdateWorkerStates s)
          None
        return DUnit
      }
    | _ -> incorrectArgs ())

let types : List<BuiltInType> =
  [ { name = typ "Canvas" "Meta" 0
      typeParams = []
      definition =
        CustomType.Record({ id = 1UL; name = "id"; typ = TUuid }, [])
      description = "Metadata about a canvas" }
    { name = typ "Canvas" "DB" 0
      typeParams = []
      definition =
        CustomType.Record(
          { id = 2UL; name = "name"; typ = TString },
          [ { id = 3UL; name = "tlid"; typ = TString } ]
        )
      description = "A database on a canvas" }
    { name = typ "Canvas" "HttpHandler" 0
      typeParams = []
      definition =
        CustomType.Record(
          { id = 2UL; name = "method"; typ = TString },
          [ { id = 3UL; name = "route"; typ = TString }
            { id = 4UL; name = "tlid"; typ = TString } ]
        )
      description = "An HTTP handler on a canvas" }
    { name = typ "Canvas" "Program" 0
      typeParams = []
      definition =
        CustomType.Record(
          { id = 1UL; name = "id"; typ = TUuid },
          [ { id = 2UL
              name = "dbs"
              typ = TList(TCustomType(FQTypeName.Stdlib (typ "Canvas" "DB" 0), [])) }
            { id = 3UL
              name = "httpHandlers"
              typ = TList(TCustomType(FQTypeName.Stdlib (typ "Canvas" "HttpHandler" 0), [])) } ]
        )
      description = "A program on a canvas" }
    { name = typ "Canvas" "F404" 0
      typeParams = []
      definition =
        CustomType.Record(
          { id = 1UL; name = "space"; typ = TString },
          [ { id = 3UL; name = "path"; typ = TString };
            { id = 4UL; name = "modifier"; typ = TString};
            { id = 5UL; name = "timestamp"; typ = TDateTime};
            { id = 6UL; name = "traceID"; typ = TUuid} ])
      description = "A 404 trace" }
      ]


let fns : List<BuiltInFn> =
  [ { name = fn "DarkInternal" "createUser" 0
      typeParams = []
      parameters = []
      returnType = TUuid
      description = "Creates a user, and returns their userID."
      fn =
        internalFn (function
          | _, _, [] ->
            uply {
              let! canvasID = Account.createUser ()
              return DUuid canvasID
            }
          | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    { name = fn "DarkInternal" "getAllCanvasIDs" 0
      typeParams = []
      parameters = []
      returnType = TList TUuid
      description = "Get a list of all canvas IDs"
      fn =
        internalFn (function
          | _, _, [] ->
            uply {
              let! hosts = Canvas.allCanvasIDs ()
              return hosts |> List.map DUuid |> DList
            }
          | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    { name = fn "DarkInternal" "getOwner" 0
      typeParams = []
      parameters = [ Param.make "canvasID" TUuid "" ]
      returnType = TString
      description = "Get the owner of a canvas"
      fn =
        internalFn (function
          | _, _, [ DUuid canvasID ] ->
            uply {
              let! owner = Canvas.getOwner canvasID
              return DUuid owner
            }
          | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    { name = fn "DarkInternal" "dbs" 0
      typeParams = []
      parameters = [ Param.make "canvasID" TUuid "" ]
      returnType = TList TString
      description = "Returns a list of toplevel ids of dbs in <param canvasName>"
      fn =
        internalFn (function
          | _, _, [ DUuid canvasID ] ->
            uply {
              let! dbTLIDs =
                Sql.query
                  "SELECT tlid
                     FROM toplevel_oplists_v0
                    WHERE canvas_id = @canvasID
                      AND tipe = 'db'"
                |> Sql.parameters [ "canvasID", Sql.uuid canvasID ]
                |> Sql.executeAsync (fun read -> read.tlid "tlid")
              return dbTLIDs |> List.map int64 |> List.map DInt |> DList
            }
          | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    { name = fn "DarkInternal" "domainsForCanvasID" 0
      typeParams = []
      parameters = [ Param.make "canvasID" TUuid "" ]
      returnType = TList TString
      description = "Returns the domain for a canvas if it exists"
      fn =
        internalFn (function
          | _, _, [ DUuid canvasID ] ->
            uply {
              let! name = Canvas.domainsForCanvasID canvasID
              return name |> List.map DString |> DList
            }
          | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    { name = fn "DarkInternal" "canvasIDForDomain" 0
      typeParams = []
      parameters = [ Param.make "domain" TString "" ]
      returnType = TResult(TUuid, TString)
      description = "Returns the canvasID for a domain if it exists"
      fn =
        internalFn (function
          | _, _, [ DString domain ] ->
            uply {
              let! name = Canvas.canvasIDForDomain domain
              match name with
              | Some name -> return DResult(Ok(DUuid name))
              | None -> return DResult(Error(DString "Canvas not found"))
            }
          | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    { name = fn "DarkInternal" "log" 0
      typeParams = []
      parameters =
        [ Param.make "level" TString ""
          Param.make "name" TString ""
          Param.make "log" (TDict TString) "" ]
      returnType = TDict TString
      description =
        "Write the log object to a honeycomb log, along with whatever enrichment the backend provides. Returns its input"
      fn =
        internalFn (function
          | _, _, [ DString level; DString name; DDict log as result ] ->
            let args =
              log
              |> Map.toList
              // We could just leave the dval vals as strings and use params, but
              // then we can't do numeric things (MAX, AVG, >, etc) with these
              // logs
              |> List.map (fun (k, v) -> (k, DvalReprDeveloper.toRepr v :> obj))
            Telemetry.addEvent name (("level", level) :: args)
            Ply result
          | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    { name = fn "DarkInternal" "allFunctions" 0
      typeParams = []
      parameters = []
      returnType = TList varA
      description =
        "Returns a list of objects, representing the functions available in the standard library. Does not return DarkInternal functions"
      fn =
        let rec typeName (t : DType) : string =
          match t with
          | TInt -> "Int"
          | TFloat -> "Float"
          | TBool -> "Bool"
          | TUnit -> "Unit"
          | TChar -> "Char"
          | TString -> "String"
          | TList _ -> "List"
          | TTuple _ -> "Tuple"
          | TDict _ -> "Dict"
          | TRecord _ -> "Dict"
          | TFn _ -> "block"
          | TVariable _ -> "Any"
          | TIncomplete -> "Incomplete"
          | TError -> "Error"
          | THttpResponse _ -> "Response"
          | TDB _ -> "Datastore"
          | TDateTime -> "DateTime"
          | TPassword -> "Password"
          | TUuid -> "Uuid"
          | TOption _ -> "Option"
          | TResult _ -> "Result"
          | TBytes -> "Bytes"
          | TCustomType (t, typeArgs) ->
            let typeArgsPortion =
              match typeArgs with
              | [] -> ""
              | args ->
                args
                |> List.map (fun t -> typeName t)
                |> String.concat ", "
                |> fun betweenBrackets -> "<" + betweenBrackets + ">"

            match t with
            | FQTypeName.Stdlib t ->
              $"{t.module_}.{t.typ}_v{t.version}" + typeArgsPortion
            | FQTypeName.User t -> $"{t.typ}_v{t.version}" + typeArgsPortion
            | FQTypeName.Package t ->
              $"{t.owner}/{t.package}/{t.module_}{t.typ}_v{t.version}"
              + typeArgsPortion

        internalFn (function
          | state, _, [] ->
            state.libraries.stdlibFns
            |> Map.toList
            |> List.filter (fun (key, data) ->
              (not (FQFnName.isInternalFn key)) && data.deprecated = NotDeprecated)
            |> List.map (fun (key, data) ->
              let alist =
                let returnType = typeName data.returnType
                let parameters =
                  data.parameters
                  |> List.map (fun p ->
                    Dval.obj [ ("name", DString p.name)
                               ("type", DString(typeName p.typ)) ])
                [ ("name", DString(FQFnName.toString key))
                  ("documentation", DString data.description)
                  ("parameters", DList parameters)
                  ("returnType", DString returnType) ]
              Dval.obj alist)
            |> DList
            |> Ply
          | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    { name = fn "DarkInternal" "getAndLogTableSizes" 0
      typeParams = []
      parameters = []
      returnType = TDict(varA)
      // returnType = varA CLEANUP
      description =
        "Query the postgres database for the current size (disk + rowcount) of all
tables. This uses pg_stat, so it is fast but imprecise. This function is logged
in OCaml; its primary purpose is to send data to honeycomb, but also gives
human-readable data."
      fn =
        internalFn (function
          | _, _, [] ->
            uply {
              let! tableStats = Db.tableStats ()
              // Send events to honeycomb. We could save some events by sending
              // these all as a single event - tablename.disk = 1, etc - but
              // by having an event per table, it's easier to query and graph:
              // `VISUALIZE MAX(disk), MAX(rows);  GROUP BY relation`.
              // (Also, if/when we add more tables, the graph-query doesn't need
              // to be updated)
              //
              // There are ~40k minutes/month, and 20 tables, so a 1/min cron
              // would consume 80k of our 1.5B monthly events. That seems
              // reasonable.
              tableStats
              |> List.iter (fun ts ->
                Telemetry.addEvent
                  "postgres_table_sizes"
                  [ ("relation", ts.relation)
                    ("disk_bytes", ts.diskBytes)
                    ("rows", ts.rows)
                    ("disk_human", ts.diskHuman)
                    ("rows_human", ts.rowsHuman) ])
              // Reformat a bit for human-usable dval output.
              // - Example from my local dev: {
              //     Total: {
              //       disk: 835584,
              //       diskHuman: "816 kB",
              //       rows: 139,
              //       rowsHuman: 139
              //     },
              //     access: {...},
              //     ...
              // }
              return
                tableStats
                |> List.map (fun ts ->
                  (ts.relation,
                   [ ("disk_bytes", DInt(ts.diskBytes))
                     ("rows", DInt(ts.rows))
                     ("disk_human", DString ts.diskHuman)
                     ("rows_human", DString ts.rowsHuman) ]
                   |> Map
                   |> DDict))
                |> Map
                |> DDict
            }
          | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    { name = fn "DarkInternal" "raiseInternalException" 0
      typeParams = []
      parameters = [ Param.make "argument" varA "Added as a tag" ]
      returnType = TUnit
      description =
        "Raise an internal exception inside Dark. This is intended to test exceptions
        and exception tracking, not for any real use."
      fn =
        internalFn (function
          | _, _, [ arg ] ->
            Exception.raiseInternal
              "DarkInternal::raiseInternalException"
              [ "arg", arg ]
          | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    { name = fn "DarkInternal" "serverBuildHash" 0
      typeParams = []
      parameters = []
      returnType = TString
      description = "Returns the git hash of the server's current deploy"
      fn =
        internalFn (function
          | _, _, [] -> uply { return DString LibService.Config.buildHash }
          | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    // ---------------------
    // Apis - 404s
    // ---------------------
    { name = fn "DarkInternal" "delete404" 0
      typeParams = []
      parameters =
        [ Param.make "canvasID" TUuid ""
          Param.make "space" TString ""
          Param.make "path" TString ""
          Param.make "modifier" TString "" ]
      returnType = TUnit
      description = "Deletes a specific 404 for a canvas"
      fn =
        internalFn (function
          | _, _, [ DUuid canvasID; DString space; DString path; DString modifier ] ->
            uply {
              Telemetry.addTags [ "space", space
                                  "path", path
                                  "modifier", modifier ]
              do! TraceInputs.delete404s canvasID space path modifier
              return DUnit
            }
          | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    { name = fn "DarkInternal" "getRecent404s" 0
      typeParams = []
      parameters = [ Param.make "canvasID" TUuid "" ]
      returnType = TList(TCustomType(FQTypeName.Stdlib(typ "Canvas" "F404" 0), []))
      description = "Fetch a list of recent 404s"
      fn =
        internalFn (function
          | _, _, [ DUuid canvasID ] ->
            uply {
              let! f404s = TraceInputs.getRecent404s canvasID
              return
                f404s
                |> List.map (fun (space, path, modifier, instant, traceID) ->
                  [ "space", DString space
                    "path", DString path
                    "modifier", DString modifier
                    "timestamp", DDateTime(DarkDateTime.fromInstant instant)
                    "traceID",
                    DUuid(LibExecution.AnalysisTypes.TraceID.toUUID traceID) ]
                  |> Map
                  |> DDict)
                |> DList
            }
          | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    // ---------------------
    // Apis - secrets
    // ---------------------
    { name = fn "DarkInternal" "getSecrets" 0
      typeParams = []
      parameters = [ Param.make "canvasID" TUuid "" ]
      returnType = TDict TString
      description = "Get list of secrets in the canvas"
      fn =
        internalFn (function
          | _, _, [ DUuid canvasID ] ->
            uply {
              let! secrets = Secret.getCanvasSecrets canvasID
              return
                secrets
                |> List.map (fun s ->
                  DTuple(DString s.name, DString s.value, [ DInt s.version ]))
                |> DList
            }
          | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    { name = fn "DarkInternal" "deleteSecret" 0
      typeParams = []
      parameters =
        [ Param.make "canvasID" TUuid ""
          Param.make "name" TString ""
          Param.make "version" TInt "" ]
      returnType = TUnit
      description = "Delete a secret"
      fn =
        internalFn (function
          | _, _, [ DUuid canvasID; DString name; DInt version ] ->
            uply {
              do! Secret.delete canvasID name (int version)
              return DUnit
            }
          | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    { name = fn "DarkInternal" "insertSecret" 0
      typeParams = []
      parameters =
        [ Param.make "canvasID" TUuid ""
          Param.make "name" TString ""
          Param.make "value" TString ""
          Param.make "version" TString "" ]
      returnType = TResult(TUnit, TString)
      description = "Add a secret"
      fn =
        internalFn (function
          | _, _, [ DUuid canvasID; DString name; DString value; DInt version ] ->
            uply {
              try
                do! Secret.insert canvasID name value (int version)
                return DResult(Ok DUnit)
              with
              | _ -> return DResult(Error(DString "Error inserting secret"))
            }
          | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    // ---------------------
    // Apis - toplevels
    // ---------------------
    { name = fn "DarkInternal" "deleteToplevelForever" 0
      typeParams = []
      parameters = [ Param.make "canvasID" TUuid ""; Param.make "tlid" TInt "" ]
      returnType = TBool
      description =
        "Delete a toplevel forever. Requires that the toplevel already by deleted. If so, deletes and returns true. Otherwise returns false"
      fn =
        internalFn (function
          | _, _, [ DUuid canvasID; DInt tlid ] ->
            uply {
              let tlid = uint64 tlid
              let! c =
                Canvas.loadFrom Serialize.IncludeDeletedToplevels canvasID [ tlid ]
              if Map.containsKey tlid c.deletedHandlers
                 || Map.containsKey tlid c.deletedDBs
                 || Map.containsKey tlid c.deletedUserTypes
                 || Map.containsKey tlid c.deletedUserFunctions then
                do! Canvas.deleteToplevelForever canvasID tlid
                return DBool true
              else
                return DBool false
            }
          | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    // ---------------------
    // Apis - DBs
    // ---------------------
    { name = fn "DarkInternal" "unlockedDBs" 0
      typeParams = []
      parameters = [ Param.make "canvasID" TUuid "" ]
      returnType = TList TInt
      description = "Get a list of unlocked DBs"
      fn =
        internalFn (function
          | _, _, [ DUuid canvasID ] ->
            uply {
              let! unlocked = UserDB.unlocked canvasID
              return unlocked |> List.map int64 |> List.map DInt |> DList
            }
          | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    // ---------------------
    // Apis - workers
    // ---------------------
    { name = fn "DarkInternal" "getQueueCount" 0
      typeParams = []
      parameters = [ Param.make "canvasID" TUuid ""; Param.make "tlid" TInt "" ]
      returnType = TList TInt
      description = "Get count of how many events are in the queue for this tlid"
      fn =
        internalFn (function
          | _, _, [ DUuid canvasID; DInt tlid ] ->
            uply {
              let tlid = uint64 tlid
              let! count = Stats.workerStats canvasID tlid
              return DInt count
            }
          | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    { name = fn "DarkInternal" "getAllSchedulingRules" 0
      typeParams = []
      parameters = []
      returnType = TList varA
      description = "Returns a list of all queue scheduling rules"
      fn =
        internalFn (function
          | _, _, [] ->
            uply {
              let! rules = SchedulingRules.getAllSchedulingRules ()
              return rules |> List.map SchedulingRules.SchedulingRule.toDval |> DList
            }
          | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    { name = fn "DarkInternal" "getSchedulingRulesForCanvas" 0
      typeParams = []
      parameters = [ Param.make "canvasID" TUuid "" ]
      returnType = TList varA
      description =
        "Returns a list of all queue scheduling rules for the specified canvasID"
      fn =
        internalFn (function
          | _, _, [ DUuid canvasID ] ->
            uply {
              let! rules = SchedulingRules.getSchedulingRules canvasID
              return rules |> List.map SchedulingRules.SchedulingRule.toDval |> DList
            }
          | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    { name = fn "DarkInternal" "addWorkerSchedulingBlock" 0
      typeParams = []
      parameters =
        [ Param.make "canvasID" TUuid ""; Param.make "handlerName" TString "" ]
      returnType = TUnit
      description =
        "Add a worker scheduling 'block' for the given canvas and handler. This prevents any events for that handler from being scheduled until the block is manually removed."
      fn = modifySchedule Queue.blockWorker
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    { name = fn "DarkInternal" "removeWorkerSchedulingBlock" 0
      typeParams = []
      parameters =
        [ Param.make "canvasID" TUuid ""; Param.make "handlerName" TString "" ]
      returnType = TUnit
      description =
        "Removes the worker scheduling block, if one exists, for the given canvas and handler. Enqueued events from this job will immediately be scheduled."
      fn = modifySchedule Queue.unblockWorker
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    { name = fn "DarkInternal" "getOpsForToplevel" 0
      typeParams = []
      parameters = [ Param.make "canvasID" TUuid ""; Param.make "tlid" TInt "" ]
      returnType = TList TString
      description = "Returns all ops for a tlid in the given canvas"
      fn =
        internalFn (function
          | _, _, [ DUuid canvasID; DInt tlid ] ->
            uply {
              let tlid = uint64 tlid
              let! ops =
                let loadAmount = Serialize.LoadAmount.IncludeDeletedToplevels
                Serialize.loadOplists loadAmount canvasID [ tlid ]

              match ops with
              | [ (_tlid, ops) ] ->
                return ops |> List.map (string >> DString) |> DList
              | _ -> return DList []
            }
          | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    { name = fn "DarkInternal" "createCanvas" 0
      typeParams = []
      parameters = [ Param.make "owner" TUuid ""; Param.make "name" TString "" ]
      returnType = TUuid
      description = "Creates a new canvas"
      fn =
        internalFn (function
          | _, _, [ DUuid owner; DString name ] ->
            uply {
              let! canvasID = Canvas.create owner name
              return DUuid canvasID
            }
          | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    { name = fn "DarkInternal" "darkEditorCanvas" 0
      typeParams = []
      parameters = []
      returnType = TCustomType(FQTypeName.Stdlib(typ "Canvas" "Meta" 0), [])
      description = "Returns basic details of the dark-editor canvas"
      fn =
        internalFn (function
          | state, _, [] ->
            uply {
              return [ "id", DUuid state.program.canvasID ] |> Map |> DDict // TODO: DRecord
            }
          | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated }


    // TODO: this name is bad?
    { name = fn "DarkInternal" "canvasProgram" 0
      typeParams = []
      parameters = [ Param.make "canvasID" TUuid "" ]
      returnType = TResult(TCustomType(FQTypeName.Stdlib(typ "Canvas" "Program" 0), []), TString)
      description =
        "Returns a list of toplevel ids of http handlers in canvas <param canvasId>"
      fn =
        internalFn (function
          | _, _, [ DUuid canvasID ] ->
            uply {
              let! canvas = Canvas.loadAll canvasID

              let dbs =
                Map.values canvas.dbs
                |> Seq.toList
                |> List.map (fun db ->
                  [ "tlid", DString(db.tlid.ToString()); "name", DString db.name ]
                  |> Map
                  |> DDict)
                |> DList

              let httpHandlers =
                Map.values canvas.handlers
                |> Seq.toList
                |> List.choose (fun handler ->
                  match handler.spec with
                  | PT.Handler.Worker _
                  | PT.Handler.Cron _
                  | PT.Handler.REPL _ -> None
                  | PT.Handler.HTTP (route, method, _ids) ->
                    [ "tlid", DString(handler.tlid.ToString())
                      "method", DString method
                      "route", DString route ]
                    |> Map
                    |> DDict
                    |> Some)
                |> DList

              return
                DResult(Ok(DDict(Map [ "dbs", dbs; "httpHandlers", httpHandlers ])))
            }
          | _ -> incorrectArgs ())
      sqlSpec = NotQueryable
      previewable = Impure
      deprecated = NotDeprecated } ]
