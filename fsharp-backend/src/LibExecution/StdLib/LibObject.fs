module LibExecution.StdLib.LibObject

open System.Threading.Tasks
open System.Numerics
open FSharp.Control.Tasks
open FSharpPlus

open LibExecution.RuntimeTypes
open Prelude

module Errors = LibExecution.Errors

let fn = FQFnName.stdlibFnName

let err (str : string) = Value(Dval.errStr str)

let incorrectArgs = LibExecution.Errors.incorrectArgs

let varA = TVariable "a"
let varB = TVariable "b"

let fns : List<BuiltInFn> = []
// [ { name = fn "Object" "empty" 0
//    ; parameters = []
//    ; returnType = TObj
//    ; description = "Return an empty object"
//    ; fn =
//         (function _, [] -> DObj DvalMap.empty | _ -> incorrectArgs ())
//    ; sqlSpec = NotYetImplementedTODO
//    ; previewable = Pure
//    ; deprecated = ReplacedBy(fn "" "" 0) }
//  ; { name = fn "Object" "merge" 0
//    ; parameters = [Param.make "left" TObj ""; Param.make "right" TObj ""]
//    ; returnType = TObj
//    ; description =
//        "Return a combined object with both objects' keys and values. If the same key exists in both `left` and `right`, then use the value from `right`"
//    ; fn =
//          (function
//          | _, [DObj l; DObj r] ->
//              DObj (Stdlib_util.merge_right l r)
//          | _ ->
//              incorrectArgs ())
//    ; sqlSpec = NotYetImplementedTODO
//    ; previewable = Pure
//    ; deprecated = ReplacedBy(fn "" "" 0) }
//  ; { name = fn "Object" "toJSON" 0
//    ; parameters = [Param.make "obj" TObj ""]
//    ; returnType = TStr
//    ; description = "Dumps `obj` to a JSON string"
//    ; fn =
//          (function
//          | _, [DObj o] ->
//              DObj o
//              |> Legacy.PrettyResponseJsonV0.to_pretty_response_json_v0
//              |> DStr
//          | _ ->
//              incorrectArgs ())
//    ; sqlSpec = NotYetImplementedTODO
//    ; previewable = Pure
//    ; deprecated = ReplacedBy(fn "" "" 0) }
//  ; { name = fn "Object" "toJSON" 1
//    ; parameters = [Param.make "obj" TObj ""]
//    ; returnType = TStr
//    ; description = "Dumps `obj` to a JSON string"
//    ; fn =
//          (function
//          | _, [DObj o] ->
//              DObj o
//              |> Dval.to_pretty_machine_json_v1
//              |> DStr
//          | _ ->
//              incorrectArgs ())
//    ; sqlSpec = NotYetImplementedTODO
//    ; previewable = Pure
//    ; deprecated = ReplacedBy(fn "" "" 0) } ]
//
