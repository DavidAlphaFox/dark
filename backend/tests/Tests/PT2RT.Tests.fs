module Tests.ProgramTypesToRuntimeTypes

open Expecto
open Prelude
open TestUtils.TestUtils

module PT = LibExecution.ProgramTypes
module RT = LibExecution.RuntimeTypes
module VT = LibExecution.ValueType
module PT2RT = LibExecution.ProgramTypesToRuntimeTypes
module PackageIDs = LibExecution.PackageIDs

module E = TestValues.Expressions
module PM = TestValues.PM

open TestUtils.PTShortcuts

// TODO: consider adding an Expect.equalInstructions,
// which better points out the diffs in the lists

// TODO we need tests of PT2RT-ing *functions* as well.

module Expr =
  let t name expr expected =
    testTask name {
      let actual = PT2RT.Expr.toRT Map.empty 0 expr
      let actual = (actual.registerCount, actual.instructions, actual.resultIn)
      return Expect.equal actual expected ""
    }

  module Basic =
    let one = t "1" E.Basic.one (1, [ RT.LoadVal(0, RT.DInt64 1L) ], 0)

    // let onePlusTwo =
    //   t
    //     "1+2"
    //     E.Basic.onePlusTwo
    //     (4,
    //      [ RT.LoadVal(
    //          0,
    //          RT.DFnVal(
    //            RT.AppNamedFn(RT.FQFnName.Builtin { name = "int64Add"; version = 0 })
    //          )
    //        )
    //        RT.LoadVal(1, RT.DInt64 1L)
    //        RT.LoadVal(2, RT.DInt64 2L)
    //        RT.Apply(3, 0, [], { head = 1; tail = [ 2 ] }) ],
    //      3)

    let tests =
      testList
        "Basic"
        [ one
          //onePlusTwo
          ]


  module Let =
    let simple =
      t
        "let x = true\n x"
        E.Let.simple
        (2,
         [ RT.LoadVal(0, RT.DBool true)
           RT.CheckLetPatternAndExtractVars(0, RT.LPVariable 1) ],
         1)

    let tuple =
      t
        "let (x, y) = (1, 2)\nx"
        E.Let.tuple
        (5,
         [ // register 0 isn't exposed, but used to temporarily store the tuple
           RT.LoadVal(1, RT.DInt64 1L)
           RT.LoadVal(2, RT.DInt64 2L)
           RT.CreateTuple(0, 1, 2, [])

           RT.CheckLetPatternAndExtractVars(
             0,
             RT.LPTuple(RT.LPVariable 3, RT.LPVariable 4, [])
           ) ],
         3)

    let tupleNested =
      t
        "let (a, (b, c)) = (1, (2, 3)) in b"
        E.Let.tupleNested
        (8,
         [ // reserve 0 for outer tuple
           RT.LoadVal(1, RT.DInt64 1L)
           // reserve 2 for inner tuple
           RT.LoadVal(3, RT.DInt64 2L)
           RT.LoadVal(4, RT.DInt64 3L)
           RT.CreateTuple(2, 3, 4, []) // create inner tuple
           RT.CreateTuple(0, 1, 2, []) // create outer tuple
           RT.CheckLetPatternAndExtractVars(
             0,
             RT.LPTuple(
               RT.LPVariable 5,
               RT.LPTuple(RT.LPVariable 6, RT.LPVariable 7, []),
               []
             )
           ) ],
         6)

    let undefinedVar = t "a" E.Let.undefinedVar (1, [ RT.VarNotFound(0, "a") ], 0)

    let tests = testList "Let" [ simple; tuple; tupleNested; undefinedVar ]


  module List =
    let simple =
      t
        "[true, false, true]"
        E.List.simple
        (4,
         [ RT.LoadVal(1, RT.DBool true)
           RT.LoadVal(2, RT.DBool false)
           RT.LoadVal(3, RT.DBool true)
           RT.CreateList(0, [ 1; 2; 3 ]) ],
         0)

    let nested =
      t
        "[[true; false]; [false; true]]"
        E.List.nested
        (7,
         [ // first inner list
           RT.LoadVal(2, RT.DBool true)
           RT.LoadVal(3, RT.DBool false)
           RT.CreateList(1, [ 2; 3 ])

           // second inner list
           RT.LoadVal(5, RT.DBool false)
           RT.LoadVal(6, RT.DBool true)
           RT.CreateList(4, [ 5; 6 ])

           // outer list
           RT.CreateList(0, [ 1; 4 ]) ],
         0)

    let mixed =
      t
        "[1, true]"
        E.List.mixed
        (3,
         [ RT.LoadVal(1, RT.DInt64 1L)
           RT.LoadVal(2, RT.DBool true)
           RT.CreateList(0, [ 1; 2 ]) ],
         0)

    let tests = testList "Lists" [ simple; nested; mixed ]


  module String =
    let simple =
      t "[\"hello\"]" E.String.simple (1, [ RT.LoadVal(0, RT.DString "hello") ], 0)

    let withInterpolation =
      t
        "[let x = \"world\"\n$\"hello {x}\"]"
        E.String.withInterpolation
        (3,
         [ RT.LoadVal(0, RT.DString ", world")
           RT.CheckLetPatternAndExtractVars(0, RT.LPVariable 1)

           RT.CreateString(2, [ RT.Text "hello"; RT.Interpolated 1 ]) ],
         2)

    let tests = testList "String" [ simple; withInterpolation ]


  module Dict =
    let empty = t "Dict {}" E.Dict.empty (1, [ RT.CreateDict(0, []) ], 0)

    let simple =
      t
        "Dict { t: true}"
        E.Dict.simple
        (2, [ RT.LoadVal(1, RT.DBool true); RT.CreateDict(0, [ ("key", 1) ]) ], 0)

    let multEntries =
      t
        "Dict {t: true; f: false}"
        E.Dict.multEntries
        (3,
         [ RT.LoadVal(1, RT.DBool true)
           RT.LoadVal(2, RT.DBool false)
           RT.CreateDict(0, [ ("t", 1); ("f", 2) ]) ],
         0)

    let dupeKey =
      t
        "Dict {t: true; f: false; t: true}"
        E.Dict.dupeKey
        (4,
         [ RT.LoadVal(1, RT.DBool true)
           RT.LoadVal(2, RT.DBool false)
           RT.LoadVal(3, RT.DBool false)
           RT.CreateDict(0, [ ("t", 1); ("f", 2); ("t", 3) ]) ],
         0)

    let tests = testList "Dict" [ empty; simple; multEntries; dupeKey ]


  module If =
    let gotoThenBranch =
      t
        "if true then 1 else 2"
        E.If.gotoThenBranch
        (4,
         [ // reserve register 0 for the result

           // cond
           RT.LoadVal(1, RT.DBool true)
           RT.JumpByIfFalse(3, 1)

           // then
           RT.LoadVal(2, RT.DInt64 1L)
           RT.CopyVal(0, 2)
           RT.JumpBy 2

           // else
           RT.LoadVal(3, RT.DInt64 2L)
           RT.CopyVal(0, 3) ],
         0)


    let gotoElseBranch =
      t
        "if false then 1 else 2"
        E.If.gotoElseBranch
        (4,
         [ // cond
           RT.LoadVal(1, RT.DBool false)
           RT.JumpByIfFalse(3, 1)

           // then
           RT.LoadVal(2, RT.DInt64 1L)
           RT.CopyVal(0, 2)
           RT.JumpBy 2

           // else
           RT.LoadVal(3, RT.DInt64 2L)
           RT.CopyVal(0, 3) ],
         0)

    let elseMissing =
      t
        "if false then 1"
        E.If.elseMissing
        (3,
         [ RT.LoadVal(0, RT.DUnit)
           RT.LoadVal(1, RT.DBool false)
           RT.JumpByIfFalse(2, 1)
           RT.LoadVal(2, RT.DInt64 1L)
           RT.CopyVal(0, 2) ],
         0)

    let tests = testList "If" [ gotoThenBranch; gotoElseBranch; elseMissing ]


  module Tuples =
    let two =
      t
        "(false, true)"
        E.Tuples.two
        (3,
         [ RT.LoadVal(1, RT.DBool false)
           RT.LoadVal(2, RT.DBool true)
           RT.CreateTuple(0, 1, 2, []) ],
         0)

    let three =
      t
        "(false, true, false)"
        E.Tuples.three
        (4,
         [ RT.LoadVal(1, RT.DBool false)
           RT.LoadVal(2, RT.DBool true)
           RT.LoadVal(3, RT.DBool false)
           RT.CreateTuple(0, 1, 2, [ 3 ]) ],
         0)

    let nested =
      t
        "((false, true), true, (true, false))"
        E.Tuples.nested
        (8,
         [ // 0 "reserved" for outer tuple

           // first inner tuple (1 "reserved")
           RT.LoadVal(2, RT.DBool false)
           RT.LoadVal(3, RT.DBool true)
           RT.CreateTuple(1, 2, 3, [])

           // middle value
           RT.LoadVal(4, RT.DBool true)

           // second inner tuple (5 "reserved")
           RT.LoadVal(6, RT.DBool true)
           RT.LoadVal(7, RT.DBool false)
           RT.CreateTuple(5, 6, 7, [])

           // wrap all in outer tuple
           RT.CreateTuple(0, 1, 4, [ 5 ]) ],
         0)

    let tests = testList "Tuples" [ two; three; nested ]


  module Match =
    let simple =
      t
        "match true with\n| false -> \"first branch\"\n| true -> \"second branch\""
        E.Match.simple
        (3,
         [ // handle the value we're matching on
           RT.LoadVal(0, RT.DBool true)

           // FIRST BRANCH
           RT.CheckMatchPatternAndExtractVars(0, RT.MPBool false, 3)
           // rhs
           RT.LoadVal(2, RT.DString "first branch")
           RT.CopyVal(1, 2)
           RT.JumpBy 5

           // SECOND BRANCH
           RT.CheckMatchPatternAndExtractVars(0, RT.MPBool true, 3)
           // rhs
           RT.LoadVal(2, RT.DString "second branch")
           RT.CopyVal(1, 2)
           RT.JumpBy 1

           // handle the case where no branches match
           RT.MatchUnmatched ],
         1)

    let notMatched =
      t
        "match true with\n| false -> \"first branch\""
        E.Match.notMatched
        (3,
         [ // handle the value we're matching on
           RT.LoadVal(0, RT.DBool true)

           // FIRST BRANCH
           RT.CheckMatchPatternAndExtractVars(0, RT.MPBool false, 3)
           // rhs
           RT.LoadVal(2, RT.DString "first branch")
           RT.CopyVal(1, 2)
           RT.JumpBy 1

           // handle the case where no branches match
           RT.MatchUnmatched ],
         1)

    let withVar =
      t
        "match true with\n| x -> x"
        E.Match.withVar
        (3,
         [ RT.LoadVal(0, RT.DBool true)

           RT.CheckMatchPatternAndExtractVars(0, RT.MPVariable 2, 2)
           RT.CopyVal(1, 2)
           RT.JumpBy 1

           RT.MatchUnmatched ],
         1)

    // let withVarAndWhenCondition =
    //   t
    //     "match 4 with\n| 1 -> \"first branch\"\n| x when x % 2 == 0 -> \"second branch\""
    //     E.Match.withVarAndWhenCondition
    //     (10,
    //      [ RT.LoadVal(0, RT.DInt64 4L)

    //        // first branch
    //        RT.CheckMatchPatternAndExtractVars(0, RT.MPInt64 1L, 5)
    //        RT.LoadVal(2, RT.DString "")
    //        RT.LoadVal(3, RT.DString "first branch")
    //        RT.AppendString(2, 3)
    //        RT.CopyVal(1, 2)
    //        RT.JumpBy 14

    //        // second branch
    //        RT.CheckMatchPatternAndExtractVars(0, RT.MPVariable "x", 12)
    //        RT.LoadVal(2, (RT.DFnVal(RT.AppNamedFn(RT.FQFnName.fqBuiltin "equals" 0))))
    //        RT.LoadVal(3, (RT.DFnVal(RT.AppNamedFn(RT.FQFnName.fqBuiltin "int64Mod" 0))))
    //        RT.GetVar(4, "x")
    //        RT.Apply(5, 3, [], NEList.ofList 4 [])
    //        RT.LoadVal(6, RT.DInt64 2L)
    //        RT.Apply(7, 2, [], NEList.ofList 5 [ 6 ])
    //        RT.JumpByIfFalse(5, 7)
    //        RT.LoadVal(8, RT.DString "")
    //        RT.LoadVal(9, RT.DString "second branch")
    //        RT.AppendString(8, 9)
    //        RT.CopyVal(1, 8)
    //        RT.JumpBy 1

    //        // handle the case where no branches match
    //        RT.MatchUnmatched ],
    //      1)

    let list =
      t
        "match [1, 2] with\n| [1, 2] -> \"first branch\""
        E.Match.list
        (5,
         [ // expr, whose result we store in 0
           RT.LoadVal(1, RT.DInt64 1L)
           RT.LoadVal(2, RT.DInt64 2L)
           RT.CreateList(0, [ 1; 2 ])

           // first branch
           RT.CheckMatchPatternAndExtractVars(
             0,
             RT.MPList [ RT.MPInt64 1L; RT.MPInt64 2L ],
             3
           )
           RT.LoadVal(4, RT.DString "first branch")
           RT.CopyVal(3, 4)
           RT.JumpBy 1

           // handle the case where no branches match
           RT.MatchUnmatched ],
         3)

    let listCons =
      t
        "match [1, 2] with\n| 1 :: tail -> tail"
        E.Match.listCons
        (5,
         [ // expr, whose result we store in 0
           RT.LoadVal(1, RT.DInt64 1L)
           RT.LoadVal(2, RT.DInt64 2L)
           RT.CreateList(0, [ 1; 2 ])

           // first branch
           RT.CheckMatchPatternAndExtractVars(
             0,
             RT.MPListCons(RT.MPInt64 1L, RT.MPVariable 4),
             2
           )
           RT.CopyVal(3, 4)
           RT.JumpBy 1

           // handle the case where no branches match
           RT.MatchUnmatched ],
         3)

    let tuple =
      t
        "match (1, 2) with\n| (1, 2) -> \"first branch\""
        E.Match.tuple
        (5,
         [ // expr, whose result we store in 0
           RT.LoadVal(1, RT.DInt64 1L)
           RT.LoadVal(2, RT.DInt64 2L)
           RT.CreateTuple(0, 1, 2, [])

           // first branch
           RT.CheckMatchPatternAndExtractVars(
             0,
             RT.MPTuple(RT.MPInt64 1L, RT.MPInt64 2L, []),
             3
           )
           RT.LoadVal(4, RT.DString "first branch")
           RT.CopyVal(3, 4)
           RT.JumpBy 1

           // handle the case where no branches match
           RT.MatchUnmatched ],
         3)

    let tests =
      testList
        "Match"
        [ simple
          notMatched
          withVar
          //withVarAndWhenCondition // -- disabled because of fn-calling issues
          list
          listCons
          tuple ]

  module Pipes =
    let lambda =
      t
        "1 |> fun x -> x"
        E.Pipes.lambda
        (3,
         [ RT.CreateLambda(
             0,
             { exprId = E.Pipes.pipeID
               patterns = NEList.ofList (RT.LPVariable 0) []
               registersToCloseOver = []
               instructions = { registerCount = 1; instructions = []; resultIn = 0 } }
           )
           RT.LoadVal(1, RT.DInt64 1L)
           RT.Apply(2, 0, [], NEList.ofList 1 []) ],
         2)

    let infix =
      t
        "1 |> (+) 2"
        E.Pipes.infix
        (4,
         [ RT.LoadVal(0, RT.DInt64 1L)
           RT.LoadVal(1, RT.DInt64 2L)
           RT.LoadVal(
             2,
             RT.DApplicable(
               RT.AppNamedFn
                 { name = RT.FQFnName.fqBuiltin "int64Add" 0
                   typeArgs = []
                   argsSoFar = [] }
             )
           )
           RT.Apply(3, 2, [], NEList.ofList 0 [ 1 ]) ],
         3)

    let fnCall =
      // why are we loading the fn first?
      t
        "1 |> Builtin.int64Add 2"
        E.Pipes.fnCall
        (4,
         [ RT.LoadVal(
             0,
             RT.DApplicable(
               RT.AppNamedFn
                 { name = RT.FQFnName.fqBuiltin "int64Add" 0
                   typeArgs = []
                   argsSoFar = [] }
             )
           )
           RT.LoadVal(1, RT.DInt64 1L)
           RT.LoadVal(2, RT.DInt64 2L)
           RT.Apply(3, 0, [], NEList.ofList 1 [ 2 ]) ],
         3)

    let variable =
      t
        "let myLambda = fun x -> x + 1\n1 |> myLambda"
        E.Pipes.variable
        (4,
         [ RT.CreateLambda(
             0,
             { exprId = E.Pipes.lambdaID
               patterns = NEList.ofList (RT.LPVariable 0) []
               registersToCloseOver = []
               instructions =
                 { registerCount = 4
                   instructions =
                     [ RT.LoadVal(1, RT.DInt64 1L)
                       RT.LoadVal(
                         2,
                         RT.DApplicable(
                           RT.AppNamedFn
                             { name = RT.FQFnName.fqBuiltin "int64Add" 0
                               typeArgs = []
                               argsSoFar = [] }
                         )
                       )
                       RT.Apply(3, 2, [], NEList.ofList 0 [ 1 ]) ]
                   resultIn = 3 } }
           )
           RT.CheckLetPatternAndExtractVars(0, RT.LPVariable 1)
           RT.LoadVal(2, RT.DInt64 1L)
           RT.Apply(3, 1, [], NEList.ofList 2 []) ],
         3)

    let multiple =
      t
        "let incr = fun x -> x + 1\n2 |> incr |> fun x -> x * 2 |> Builtin.int64Add 3 |> (+) 4"
        E.Pipes.multiple
        (12,
         [ RT.CreateLambda(
             0,
             { exprId = E.Pipes.lambdaID
               patterns = NEList.ofList (RT.LPVariable 0) []
               registersToCloseOver = []
               instructions =
                 { registerCount = 4
                   instructions =
                     [ RT.LoadVal(1, RT.DInt64 1L)
                       RT.LoadVal(
                         2,
                         (RT.DApplicable(
                           RT.AppNamedFn
                             { name = RT.FQFnName.fqBuiltin "int64Add" 0
                               typeArgs = []
                               argsSoFar = [] }
                         ))
                       )
                       RT.Apply(3, 2, [], NEList.ofList 0 [ 1 ]) ]
                   resultIn = 3 } }
           )
           RT.CheckLetPatternAndExtractVars(0, RT.LPVariable 1)
           RT.LoadVal(
             2,
             RT.DApplicable(
               RT.AppNamedFn
                 { name = RT.FQFnName.fqBuiltin "int64Add" 0
                   typeArgs = []
                   argsSoFar = [] }
             )
           )
           RT.CreateLambda(
             3,
             { exprId = E.Pipes.pipeID
               patterns = NEList.ofList (RT.LPVariable 0) []
               registersToCloseOver = []
               instructions =
                 { registerCount = 4
                   instructions =
                     [ RT.LoadVal(1, RT.DInt64 2L)
                       RT.LoadVal(
                         2,
                         (RT.DApplicable(
                           RT.AppNamedFn
                             { name = RT.FQFnName.fqBuiltin "int64Multiply" 0
                               typeArgs = []
                               argsSoFar = [] }
                         ))
                       )
                       RT.Apply(3, 2, [], NEList.ofList 0 [ 1 ]) ]
                   resultIn = 3 } }
           )
           RT.LoadVal(4, RT.DInt64 2L)
           RT.Apply(5, 1, [], NEList.ofList 4 [])
           RT.Apply(6, 3, [], NEList.ofList 5 [])
           RT.LoadVal(7, RT.DInt64 3L)
           RT.Apply(8, 2, [], NEList.ofList 6 [ 7 ])
           RT.LoadVal(9, RT.DInt64 4L)
           RT.LoadVal(
             10,
             RT.DApplicable(
               RT.AppNamedFn
                 { name = RT.FQFnName.fqBuiltin "int64Add" 0
                   typeArgs = []
                   argsSoFar = [] }
             )
           )
           RT.Apply(11, 10, [], NEList.ofList 8 [ 9 ]) ],
         11)



    let tests = testList "Pipes" [ lambda; infix; fnCall; variable; multiple ]


  module Enums =
    let simple =
      t
        "Test.Color.Blue"
        E.Enums.simple
        (1,
         [ RT.CreateEnum(
             0,
             RT.FQTypeName.fqPackage PM.Types.Enums.withoutFields,
             [],
             "Blue",
             []
           ) ],
         0)

    let withFields =
      t
        "Test.MyOption.Some 1"
        E.Enums.withFields
        (2,
         [ RT.LoadVal(1, RT.DInt64 1L)
           RT.CreateEnum(
             0,
             RT.FQTypeName.fqPackage PM.Types.Enums.withFields,
             [],
             "Some",
             [ 1 ]
           ) ],
         0)

    let tests = testList "Enums" [ simple; withFields ]


  module Records =
    let simple =
      t
        "Test.Test { key = true }"
        E.Records.simple
        (2,
         [ RT.LoadVal(1, RT.DBool true)
           RT.CreateRecord(
             0,
             RT.FQTypeName.fqPackage PM.Types.Records.singleField,
             [],
             [ ("key", 1) ]
           ) ],
         0)

    let nested =
      t
        "Test.Test2 { outer = (Test.Test { key = true }) }"
        E.Records.nested
        (3,
         [ RT.LoadVal(2, RT.DBool true)

           // inner record
           RT.CreateRecord(
             1,
             RT.FQTypeName.fqPackage PM.Types.Records.singleField,
             [],
             [ ("key", 2) ]
           )

           // outer record
           RT.CreateRecord(
             0,
             RT.FQTypeName.fqPackage PM.Types.Records.nested,
             [],
             [ ("outer", 1) ]
           ) ],
         0)

    let tests = testList "Records" [ simple; nested ]


  module RecordFieldAccess =
    let simple =
      t
        "let r = Test.Test { key = true }\nr.key"
        E.RecordFieldAccess.simple
        (3,
         [ RT.LoadVal(1, RT.DBool true)
           RT.CreateRecord(
             0,
             RT.FQTypeName.fqPackage PM.Types.Records.singleField,
             [],
             [ ("key", 1) ]
           )
           RT.GetRecordField(2, 0, "key") ],
         2)

    let notRecord =
      t
        "1.key"
        E.RecordFieldAccess.notRecord
        (2, [ RT.LoadVal(0, RT.DInt64 1L); RT.GetRecordField(1, 0, "key") ], 1)

    let missingField =
      t
        "(Test.Test { key = true }).missing"
        E.RecordFieldAccess.missingField
        (3,
         [ RT.LoadVal(1, RT.DBool true)
           RT.CreateRecord(
             0,
             RT.FQTypeName.fqPackage PM.Types.Records.singleField,
             [],
             [ ("key", 1) ]
           )
           RT.GetRecordField(2, 0, "missing") ],
         2)

    let nested =
      t
        "(Test.Test2 { outer = Test.Test { key = true } }).outer.key"
        E.RecordFieldAccess.nested
        (5,
         [ RT.LoadVal(2, RT.DBool true)
           RT.CreateRecord(
             1,
             RT.FQTypeName.fqPackage PM.Types.Records.singleField,
             [],
             [ ("key", 2) ]
           )

           RT.CreateRecord(
             0,
             RT.FQTypeName.fqPackage PM.Types.Records.nested,
             [],
             [ ("outer", 1) ]
           )
           RT.GetRecordField(3, 0, "outer")
           RT.GetRecordField(4, 3, "key") ],
         4)


    let tests =
      testList "RecordFieldAccess" [ simple; notRecord; missingField; nested ]


  module RecordUpdate =

    let simple =
      t
        "let r = Test.Test { key = true }\n{ r with key = false }"
        E.RecordUpdate.simple
        (4,
         [ RT.LoadVal(1, RT.DBool true)
           RT.CreateRecord(
             0,
             RT.FQTypeName.fqPackage PM.Types.Records.singleField,
             [],
             [ ("key", 1) ]
           )
           RT.LoadVal(2, RT.DBool false)
           RT.CloneRecordWithUpdates(3, 0, [ ("key", 2) ]) ],
         3)
    let notRecord =
      t
        "1 with key = false"
        E.RecordUpdate.notRecord
        (3,
         [ RT.LoadVal(0, RT.DInt64 1L)
           RT.LoadVal(1, RT.DBool false)
           RT.CloneRecordWithUpdates(2, 0, [ ("key", 1) ]) ],
         2)
    let fieldThatShouldNotExist =
      t
        "let r = Test.Test { key = true }\n{ r with bonus = false }"
        E.RecordUpdate.fieldThatShouldNotExist
        (4,
         [ RT.LoadVal(1, RT.DBool true)
           RT.CreateRecord(
             0,
             RT.FQTypeName.fqPackage PM.Types.Records.singleField,
             [],
             [ ("key", 1) ]
           )
           RT.LoadVal(2, RT.DBool false)
           RT.CloneRecordWithUpdates(3, 0, [ ("bonus", 2) ]) ],
         3)
    let fieldWithWrongType =
      t
        "let r = Test.Test { key = true }\n{ r with key = 1 }"
        E.RecordUpdate.fieldWithWrongType
        (4,
         [ RT.LoadVal(1, RT.DBool true)
           RT.CreateRecord(
             0,
             RT.FQTypeName.fqPackage PM.Types.Records.singleField,
             [],
             [ ("key", 1) ]
           )
           RT.LoadVal(2, RT.DInt64 1L)
           RT.CloneRecordWithUpdates(3, 0, [ ("key", 2) ]) ],
         3)

    let tests =
      testList
        "RecordUpdate"
        [ simple; notRecord; fieldThatShouldNotExist; fieldWithWrongType ]


  (*
  module Infix =
    module And =
      let mixed = eInfix (PT.Infix.BinOp PT.BinOpAnd) (eBool true) (eBool false)
      let nested = eInfix (PT.Infix.BinOp PT.BinOpAnd) mixed (eBool true)
      let bothTrue = eInfix (PT.Infix.BinOp PT.BinOpAnd) (eBool true) (eBool true)
      let bothFalse = eInfix (PT.Infix.BinOp PT.BinOpAnd) (eBool false) (eBool false)

    module Or =
      let mixed = eInfix (PT.Infix.BinOp PT.BinOpOr) (eBool true) (eBool false)
      let nested = eInfix (PT.Infix.BinOp PT.BinOpOr) mixed (eBool true)
      let bothTrue = eInfix (PT.Infix.BinOp PT.BinOpOr) (eBool true) (eBool true)
      let bothFalse = eInfix (PT.Infix.BinOp PT.BinOpOr) (eBool false) (eBool false)

    module Add =
      let simple =
        eInfix (PT.Infix.InfixFnCall PT.ArithmeticPlus) (eInt64 1) (eInt64 2)

    module Subtract =
      let simple =
        eInfix (PT.Infix.InfixFnCall PT.ArithmeticMinus) (eInt64 1) (eInt64 2)*)


  module Constants =
    module Package =
      let mySpecialNumber =
        t
          "Test.mySpecialNumber"
          E.Constants.Package.MySpecialNumber.usage
          (1,
           [ RT.LoadConstant(
               0,
               RT.FQConstantName.Package E.Constants.Package.MySpecialNumber.id
             ) ],
           0)
      let tests = testList "Package" [ mySpecialNumber ]
    let tests = testList "Constants" [ Package.tests ]


  module Infix =
    module And =
      let mixed =
        t
          "true && false"
          E.Infix.And.mixed
          (3,
           [ RT.LoadVal(0, RT.DBool true)
             RT.LoadVal(1, RT.DBool false)
             RT.And(2, 0, 1) ],
           2)
      let tests = testList "And" [ mixed ]

    module Add =
      let simple =
        t
          "1 + 2"
          E.Infix.Add.simple
          (4,
           [ RT.LoadVal(0, RT.DInt64 1L)
             RT.LoadVal(1, RT.DInt64 2L)
             RT.LoadVal(
               2,
               RT.DApplicable(
                 RT.AppNamedFn
                   { name = RT.FQFnName.fqBuiltin "int64Add" 0
                     typeArgs = []
                     argsSoFar = [] }
               )
             )
             RT.Apply(3, 2, [], NEList.ofList 0 [ 1 ]) ],
           3)
      let tests = testList "Add" [ simple ]


    let tests = testList "Infix" [ And.tests; Add.tests ]



  module Lambda =
    module Identity =

      let unapplied =
        t
          "fn x -> x"
          E.Lambdas.Identity.unapplied
          (1,
           [ RT.CreateLambda(
               0,
               { exprId = E.Lambdas.Identity.id
                 patterns = NEList.ofList (RT.LPVariable 0) []
                 registersToCloseOver = []
                 instructions =
                   { registerCount = 1; instructions = []; resultIn = 0 } }
             ) ],
           0)

      let applied =
        t
          "(fn x -> x) 1"
          E.Lambdas.Identity.applied
          (3,
           [ RT.CreateLambda(
               0,
               { exprId = E.Lambdas.Identity.id
                 patterns = NEList.ofList (RT.LPVariable 0) []
                 registersToCloseOver = []
                 instructions =
                   { registerCount = 1; instructions = []; resultIn = 0 } }
             )
             RT.LoadVal(1, RT.DInt64 1L)
             RT.Apply(2, 0, [], NEList.ofList 1 []) ],
           2)

      let tests = testList "Identity" [ unapplied; applied ]

    module Add =
      let unapplied =
        t
          "fn a b -> Builtin.int64Add a b"
          E.Lambdas.Add.unapplied
          (1,
           [ RT.CreateLambda(
               0,
               { exprId = E.Lambdas.Add.id
                 patterns = NEList.ofList (RT.LPVariable 0) [ RT.LPVariable 1 ]
                 registersToCloseOver = []
                 instructions =
                   { registerCount = 4
                     instructions =
                       [ RT.LoadVal(
                           2,
                           RT.DApplicable(
                             RT.AppNamedFn
                               { name = RT.FQFnName.fqBuiltin "int64Add" 0
                                 typeArgs = []
                                 argsSoFar = [] }
                           )
                         )
                         RT.Apply(3, 2, [], NEList.ofList 0 [ 1 ]) ]
                     resultIn = 3 } }
             ) ],
           0)

      let partiallyApplied =
        t
          "(fn a b -> Builtin.int64Add a b) 1"
          E.Lambdas.Add.partiallyApplied
          (3,
           [ RT.CreateLambda(
               0,
               { exprId = E.Lambdas.Add.id
                 patterns = NEList.ofList (RT.LPVariable 0) [ RT.LPVariable 1 ]
                 registersToCloseOver = []
                 instructions =
                   { registerCount = 4
                     instructions =
                       [ RT.LoadVal(
                           2,
                           RT.DApplicable(
                             RT.AppNamedFn
                               { name = RT.FQFnName.fqBuiltin "int64Add" 0
                                 typeArgs = []
                                 argsSoFar = [] }
                           )
                         )
                         RT.Apply(3, 2, [], NEList.ofList 0 [ 1 ]) ]
                     resultIn = 3 } }
             )
             RT.LoadVal(1, RT.DInt64 1L)
             RT.Apply(2, 0, [], NEList.ofList 1 []) ],
           2)


      let fullyApplied =
        t
          "(fn a b -> Builtin.int64Add a b) 1 2"
          E.Lambdas.Add.fullyApplied
          (4,
           [ RT.CreateLambda(
               0,
               { exprId = E.Lambdas.Add.id
                 patterns = NEList.ofList (RT.LPVariable 0) [ RT.LPVariable 1 ]
                 registersToCloseOver = []
                 instructions =
                   { registerCount = 4
                     instructions =
                       [ RT.LoadVal(
                           2,
                           RT.DApplicable(
                             RT.AppNamedFn
                               { name = RT.FQFnName.fqBuiltin "int64Add" 0
                                 typeArgs = []
                                 argsSoFar = [] }
                           )
                         )
                         RT.Apply(3, 2, [], NEList.ofList 0 [ 1 ]) ]
                     resultIn = 3 } }
             )
             RT.LoadVal(1, RT.DInt64 1L)
             RT.LoadVal(2, RT.DInt64 2L)
             RT.Apply(3, 0, [], NEList.ofList 1 [ 2 ]) ],
           3)


      let tests = testList "Add" [ unapplied; partiallyApplied; fullyApplied ]

    ///```fsharp
    /// let x = 5
    /// let y = 10
    /// let addFifteen = fun a -> a + x + y
    /// addFifteen 25
    /// ```
    module AddToClosedVars =
      let unapplied =
        t
          "let x = 5\nlet y=10\nfun a -> a + x + y"
          E.Lambdas.AddToClosedVars.unapplied
          (5,
           [ RT.LoadVal(0, RT.DInt64 5L)
             RT.CheckLetPatternAndExtractVars(0, RT.LPVariable 1)

             RT.LoadVal(2, RT.DInt64 10L)
             RT.CheckLetPatternAndExtractVars(2, RT.LPVariable 3)

             RT.CreateLambda(
               4,
               { exprId = E.Lambdas.AddToClosedVars.id
                 patterns = NEList.ofList (RT.LPVariable 0) []
                 registersToCloseOver = [ (1, 1); (3, 2) ]
                 instructions =
                   { registerCount = 7
                     instructions =
                       [ RT.LoadVal(
                           3,
                           RT.DApplicable(
                             RT.AppNamedFn
                               { name = RT.FQFnName.fqBuiltin "int64Add" 0
                                 typeArgs = []
                                 argsSoFar = [] }
                           )
                         )
                         RT.LoadVal(
                           4,
                           RT.DApplicable(
                             RT.AppNamedFn
                               { name = RT.FQFnName.fqBuiltin "int64Add" 0
                                 typeArgs = []
                                 argsSoFar = [] }
                           )
                         )
                         RT.Apply(5, 4, [], NEList.ofList 1 [ 2 ])
                         RT.Apply(6, 3, [], NEList.ofList 0 [ 5 ]) ]
                     resultIn = 6 } }
             ) ],
           4)

      let applied =
        t
          "let x = 5\nlet y=10\nlet addFifteen = fun a -> a + x + y\naddFifteen 25"
          E.Lambdas.AddToClosedVars.applied
          (8,
           [ RT.LoadVal(0, RT.DInt64 5L)
             RT.CheckLetPatternAndExtractVars(0, RT.LPVariable 1)

             RT.LoadVal(2, RT.DInt64 10L)
             RT.CheckLetPatternAndExtractVars(2, RT.LPVariable 3)

             RT.CreateLambda(
               4,
               { exprId = E.Lambdas.AddToClosedVars.id
                 patterns = NEList.ofList (RT.LPVariable 0) []
                 registersToCloseOver = [ (1, 1); (3, 2) ]
                 instructions =
                   { registerCount = 7
                     instructions =
                       [ RT.LoadVal(
                           3,
                           RT.DApplicable(
                             RT.AppNamedFn
                               { name = RT.FQFnName.fqBuiltin "int64Add" 0
                                 typeArgs = []
                                 argsSoFar = [] }
                           )
                         )
                         RT.LoadVal(
                           4,
                           RT.DApplicable(
                             RT.AppNamedFn
                               { name = RT.FQFnName.fqBuiltin "int64Add" 0
                                 typeArgs = []
                                 argsSoFar = [] }
                           )
                         )
                         RT.Apply(5, 4, [], NEList.ofList 1 [ 2 ])
                         RT.Apply(6, 3, [], NEList.ofList 0 [ 5 ]) ]
                     resultIn = 6 } }
             )

             RT.CheckLetPatternAndExtractVars(4, RT.LPVariable 5)

             RT.LoadVal(6, RT.DInt64 25L)

             RT.Apply(7, 5, [], NEList.ofList 6 []) ],
           7)

      let tests = testList "AddToClosedVars" [ unapplied; applied ]


    let tests =
      testList "Lambda" [ Identity.tests; Add.tests; AddToClosedVars.tests ]



  module Fns =
    module Builtin =
      let unapplied =
        t
          "Builtin.int64Add"
          E.Fns.Builtin.unapplied
          (1,
           [ RT.LoadVal(
               0,
               RT.DApplicable(
                 RT.AppNamedFn
                   { name = RT.FQFnName.fqBuiltin "int64Add" 0
                     typeArgs = []
                     argsSoFar = [] }
               )
             ) ],
           0)

      let partiallyApplied =
        t
          "Builtin.int64Add 1"
          E.Fns.Builtin.partiallyApplied
          (3,
           [ RT.LoadVal(
               0,
               RT.DApplicable(
                 RT.AppNamedFn
                   { name = RT.FQFnName.fqBuiltin "int64Add" 0
                     typeArgs = []
                     argsSoFar = [] }
               )
             )
             RT.LoadVal(1, RT.DInt64 1L)
             RT.Apply(2, 0, [], NEList.ofList 1 []) ],
           2)

      let fullyApplied =
        t
          "Builtin.int64Add 1 2"
          E.Fns.Builtin.fullyApplied
          (4,
           [ RT.LoadVal(
               0,
               RT.DApplicable(
                 RT.AppNamedFn
                   { name = RT.FQFnName.fqBuiltin "int64Add" 0
                     typeArgs = []
                     argsSoFar = [] }
               )
             )
             RT.LoadVal(1, RT.DInt64 1L)
             RT.LoadVal(2, RT.DInt64 2L)
             RT.Apply(3, 0, [], NEList.ofList 1 [ 2 ]) ],
           3)

      let twoStepApplication =
        t
          "(Builtin.int64Add 1) 2"
          E.Fns.Builtin.twoStepApplication
          (5,
           [ RT.LoadVal(
               0,
               RT.DApplicable(
                 RT.AppNamedFn
                   { name = RT.FQFnName.fqBuiltin "int64Add" 0
                     typeArgs = []
                     argsSoFar = [] }
               )
             )
             RT.LoadVal(1, RT.DInt64 1L)
             RT.Apply(2, 0, [], NEList.ofList 1 [])
             RT.LoadVal(3, RT.DInt64 2L)
             RT.Apply(4, 2, [], NEList.ofList 3 []) ],
           4)

      let tests =
        testList
          "Fns"
          [ unapplied; partiallyApplied; fullyApplied; twoStepApplication ]


    module Package =
      module MyAdd =
        let unapplied =
          t
            "Test.myAdd"
            E.Fns.Package.MyAdd.unapplied
            (1,
             [ RT.LoadVal(
                 0,
                 RT.DApplicable(
                   RT.AppNamedFn
                     { name = RT.FQFnName.fqPackage E.Fns.Package.MyAdd.id
                       typeArgs = []
                       argsSoFar = [] }
                 )
               ) ],
             0)

        let partiallyApplied =
          t
            "Test.myAdd 1"
            E.Fns.Package.MyAdd.partiallyApplied
            (3,
             [ RT.LoadVal(
                 0,
                 RT.DApplicable(
                   RT.AppNamedFn
                     { name = RT.FQFnName.fqPackage E.Fns.Package.MyAdd.id
                       typeArgs = []
                       argsSoFar = [] }
                 )
               )
               RT.LoadVal(1, RT.DInt64 1L)
               RT.Apply(2, 0, [], NEList.ofList 1 []) ],
             2)

        let fullyApplied =
          t
            "Test.myAdd 1 2"
            E.Fns.Package.MyAdd.fullyApplied
            (4,
             [ RT.LoadVal(
                 0,
                 RT.DApplicable(
                   RT.AppNamedFn
                     { name = RT.FQFnName.fqPackage E.Fns.Package.MyAdd.id
                       typeArgs = []
                       argsSoFar = [] }
                 )
               )
               RT.LoadVal(1, RT.DInt64 1L)
               RT.LoadVal(2, RT.DInt64 2L)
               RT.Apply(3, 0, [], NEList.ofList 1 [ 2 ]) ],
             3)

        let tests = testList "MyAdd" [ unapplied; partiallyApplied; fullyApplied ]

      module MyFnThatTakesALambda =
        let myMap =
          t
            "Test.myMap [1L; 2L] (fun x -> x + 1L)"
            E.Fns.Package.MyFnThatTakesALambda.fullyApplied
            (6,
             [ RT.LoadVal(
                 0,
                 RT.DApplicable(
                   RT.AppNamedFn
                     { name =
                         RT.FQFnName.fqPackage E.Fns.Package.MyFnThatTakesALambda.id
                       typeArgs = []
                       argsSoFar = [] }
                 )
               )
               RT.LoadVal(2, RT.DInt64 1L)
               RT.LoadVal(3, RT.DInt64 2L)
               RT.CreateList(1, [ 2; 3 ])
               RT.CreateLambda(
                 4,
                 { exprId = E.Fns.Package.MyFnThatTakesALambda.lambdaID
                   patterns = { head = RT.LPVariable 0; tail = [] }
                   registersToCloseOver = []
                   instructions =
                     { registerCount = 4
                       instructions =
                         [ RT.LoadVal(1, RT.DInt64 1L)
                           RT.LoadVal(
                             2,
                             RT.DApplicable(
                               RT.AppNamedFn
                                 { name = RT.FQFnName.fqBuiltin "int64Add" 0
                                   typeArgs = []
                                   argsSoFar = [] }
                             )
                           )
                           RT.Apply(3, 2, [], { head = 0; tail = [ 1 ] }) ]
                       resultIn = 3 } }
               )
               RT.Apply(5, 0, [], { head = 1; tail = [ 4 ] }) ],
             5)

        let tests = testList "MyFnThatTakesALambda" [ myMap ]

      let tests = testList "Package" [ MyAdd.tests; MyFnThatTakesALambda.tests ]

    let tests = testList "Fns" [ Builtin.tests; Package.tests ]


  let tests =
    testList
      "Expr"
      [ Basic.tests
        Let.tests
        List.tests
        String.tests
        Dict.tests
        If.tests
        Tuples.tests
        Match.tests
        Pipes.tests
        Records.tests
        RecordFieldAccess.tests
        RecordUpdate.tests
        Enums.tests
        Constants.tests
        Infix.tests
        Lambda.tests
        Fns.tests ]


module PackageFn =
  let t name fnName typeParams params' returnType expr expected =
    testTask name {
      let fn : PT.PackageFn.PackageFn =
        { id = guuid ()
          name = { owner = "Test"; modules = []; name = fnName }
          body = expr
          typeParams = typeParams
          parameters = params' |> NEList.ofListUnsafe "" []
          returnType = returnType
          description = "TODO"
          deprecated = PT.NotDeprecated }

      let actual = PT2RT.PackageFn.toRT fn |> _.body
      let actual = (actual.registerCount, actual.instructions, actual.resultIn)
      return Expect.equal actual expected ""
    }

  module Basic =
    let returnSecondParam =
      t
        "returnSecondParam"
        "returnSecondParam"
        []
        [ { name = "a"; typ = PT.TInt64; description = "TODO" }
          { name = "b"; typ = PT.TInt64; description = "TODO" } ]
        PT.TInt64
        (eVar "b")
        (2, [], 1)

    let ignoresParamsAndReturnsStr =
      t
        "ignoresParamsAndReturnsStr"
        "ignoresParamsAndReturnsStr"
        []
        [ { name = "a"; typ = PT.TInt64; description = "TODO" }
          { name = "b"; typ = PT.TInt64; description = "TODO" } ]
        PT.TInt64
        (eStr [ strText "hello" ])
        (3, [ RT.LoadVal(2, RT.DString "hello") ], 2)

    let tests =
      testList "PackageFn" [ returnSecondParam; ignoresParamsAndReturnsStr ]


  let tests = testList "PackageFn" [ Basic.tests ]


let tests = testList "ProgramTypesToRuntimeTypes" [ Expr.tests; PackageFn.tests ]
