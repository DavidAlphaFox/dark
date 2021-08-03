open Prelude
open Tester
open Introspect
module TL = Toplevel
module B = BlankOr

let run () =
  describe "Introspect" (fun () ->
      let h1tlid = gtlid () in
      let h1data =
        { ast = FluidAST.ofExpr (EBlank (gid ()))
        ; spec =
            { space = B.newF "WORKER"
            ; name = B.newF "processOrder"
            ; modifier = B.new_ () }
        ; hTLID = h1tlid
        ; pos = {x = 0; y = 0} }
      in
      let h2tlid = gtlid () in
      let dbRefID = gid () in
      let h2data =
        { ast =
            FluidAST.ofExpr
              (EFnCall
                 ( gid ()
                 , "DB::deleteAll_v1"
                 , [EVariable (dbRefID, "Books")]
                 , NoRail ))
        ; spec =
            { space = B.newF "HTTP"
            ; name = B.newF "/hello"
            ; modifier = B.newF "GET" }
        ; hTLID = h2tlid
        ; pos = {x = 0; y = 0} }
      in
      let dbtlid = gtlid () in
      let dbdata =
        { dbTLID = dbtlid
        ; dbName = B.newF "Books"
        ; cols = []
        ; version = 0
        ; oldMigrations = []
        ; activeMigration = None
        ; pos = {x = 0; y = 0} }
      in
      let dbs = TD.fromList [(dbdata.dbTLID, dbdata)] in
      let handlers =
        TD.fromList [(h1data.hTLID, h1data); (h2data.hTLID, h2data)]
      in
      test "dbsByName" (fun () ->
          expect (dbsByName dbs)
          |> toEqual (Map.add ~key:"Books" ~value:dbtlid Map.String.empty)) ;
      test "handlersByName" (fun () ->
          let v =
            handlers |> handlersByName |> Map.get ~key:"WORKER:processOrder"
          in
          expect v |> toEqual (Some h1tlid)) ;
      test "findUsagesInAST" (fun () ->
          let handlers = handlersByName handlers in
          let datastores = dbsByName dbs in
          let functions = Map.String.empty in
          let packageFunctions = Map.String.empty in
          let usages =
            match
              findUsagesInAST
                h2tlid
                ~datastores
                ~handlers
                ~functions
                ~packageFunctions
                h2data.ast
            with
            | [{refersTo; usedIn; id}] ->
                refersTo = h2tlid && usedIn = dbtlid && id == dbRefID
            | _ ->
                false
          in
          expect usages |> toEqual true) ;
      test "tlidsToUpdateUsage" (fun () ->
          let fntlid = gtlid () in
          let ops =
            [ SetHandler (h1tlid, {x = 0; y = 0}, h1data)
            ; SetExpr (h1tlid, gid (), EBlank (gid ()))
            ; SetFunction
                { ufTLID = fntlid
                ; ufMetadata =
                    { ufmName = B.newF "trollClean"
                    ; ufmParameters = []
                    ; ufmDescription = "can users put docs here?"
                    ; ufmReturnTipe = B.new_ ()
                    ; ufmInfix = false }
                ; ufAST = FluidAST.ofExpr (FluidExpression.newB ()) } ]
          in
          expect (tlidsToUpdateUsage ops) |> toEqual [h1tlid; fntlid]) ;
      test "updateAssocList from empty" (fun () ->
          expect
            (updateAssocList ~key:"a" [] ~f:(fun u ->
                 match u with Some v -> Some v | None -> Some 1))
          |> toEqual [("a", 1)]) ;
      test "updateAssocList add non existing" (fun () ->
          expect
            (updateAssocList ~key:"b" [("a", 1)] ~f:(fun u ->
                 match u with Some v -> Some v | None -> Some 1))
          |> toEqual [("a", 1); ("b", 1)]) ;
      ()) ;
  ()
