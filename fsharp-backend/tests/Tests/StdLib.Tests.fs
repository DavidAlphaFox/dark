module Tests.StdLib

// Misc tests of Stdlib (both LibBackend and LibExecution) that could not be
// tested via LibExecution.tests

open Expecto

open System.Threading.Tasks
open FSharp.Control.Tasks

open Prelude
open Prelude.Tablecloth
open Tablecloth

module RT = LibExecution.RuntimeTypes
module PT = LibExecution.ProgramTypes
module Exe = LibExecution.Execution

open TestUtils

let equalsOCaml =
  // These are hard to represent in .tests files, usually because of FakeDval behaviour
  testMany
    "equalsOCaml"
    (FuzzTests.All.ExecutePureFunctions.equalsOCaml)
    [ ((RT.FQFnName.stdlibFnName "List" "fold" 0,
        [ RT.DList [ RT.DBool true; RT.DErrorRail(RT.DInt 0L) ]
          RT.DList []
          RT.DFnVal(
            RT.Lambda { parameters = []; symtable = Map.empty; body = RT.EBlank 1UL }
          ) ]),
       true)
      ((RT.FQFnName.stdlibFnName "Result" "fromOption" 0,
        [ RT.DOption(
            Some(
              RT.DFnVal(
                RT.Lambda
                  { parameters = []
                    symtable = Map.empty
                    body = RT.EFloat(84932785UL, -9.223372037e+18) }
              )
            )
          )
          RT.DStr "s" ]),
       true)
      ((RT.FQFnName.stdlibFnName "Result" "fromOption" 0,
        [ RT.DOption(
            Some(
              RT.DFnVal(
                RT.Lambda
                  { parameters = []
                    symtable = Map.empty
                    body =
                      RT.EMatch(
                        gid (),
                        RT.ENull(gid ()),
                        [ (RT.PFloat(gid (), -9.223372037e+18), RT.ENull(gid ())) ]
                      ) }
              )
            )
          )
          RT.DStr "s" ]),
       true)

      ]

let oldFunctionsAreDeprecated =
  test "old functions are deprecated" {

    let counts = ref Map.empty

    let fns =
      LibTest.fns @ LibExecutionStdLib.StdLib.fns @ BackendOnlyStdLib.StdLib.fns

    fns
    |> List.iter
         (fun fn ->
           let key = string { fn.name with version = 0 }

           if fn.deprecated = RT.NotDeprecated then
             counts
             := Map.update
                  key
                  (fun count -> count |> Option.defaultValue 0 |> (+) 1 |> Some)
                  !counts

           ())

    Map.iter
      (fun name count ->
        Expect.equal count 1 $"{name} has more than one undeprecated function")
      !counts
  }

let intInfixMatch =
  test "int infix functions match" {
    let actual = LibExecution.Errors.intInfixFns
    let expected =
      LibExecutionStdLib.StdLib.infixFnMapping
      |> Map.filterWithIndex (fun name _ -> name.module_ = "Int")
      |> Map.values
      |> List.map (fun name -> name.function_)
      |> Set

    Expect.equal actual expected "We didn't miss any infix functions"
  }


// FSTODO
// let t_dark_internal_fns_are_internal () =
//   let ast = fn "DarkInternal::checkAccess" [] in
//   let check_access canvas_name =
//     match exec_ast ~canvas_name ast with DError _ -> None | dval -> Some dval
//   in
//   AT.check
//     (AT.list (AT.option at_dval))
//     "DarkInternal:: functions are internal."
//     [check_access "test"; check_access "test_admin"]
//     [None; Some DNull]

let tests = testList "stdlib" [ equalsOCaml; oldFunctionsAreDeprecated; intInfixMatch ]
