module LibExecution.StdLib.LibFloat

open LibExecution.RuntimeTypes
open Prelude
open System

module Errors = LibExecution.Errors

let fn = FQFnName.stdlibFnName

let incorrectArgs = LibExecution.Errors.incorrectArgs

(* type coerces one list to another using a function *)
// let list_coerce ~(f : dval -> 'a option) (l : dval list) :
//     ('a list, dval list * dval) Result.t =
//   l
//   |> List.map (fun dv ->
//          match f dv with Some v -> Result.Ok v | None -> Result.Error (l, dv))
//   |> Result.all


// let ( >>| ) = Result.( >>| )

let fns : List<BuiltInFn> =
  [ { name = fn "Float" "ceiling" 0
      parameters = [ Param.make "a" TFloat "" ]
      returnType = TInt
      description = "Round up to an integer value"
      fn =
        (function
        | _, [ DFloat a ] -> bigint (Math.Ceiling a) |> DInt |> Value
        | _ -> incorrectArgs ())
      sqlSpec = NotYetImplementedTODO
      previewable = Pure
      deprecated = NotDeprecated }
    { name = fn "Float" "roundUp" 0
      parameters = [ Param.make "a" TFloat "" ]
      returnType = TInt
      description = "Round up to an integer value"
      fn =
        (function
        | _, [ DFloat a ] -> bigint (Math.Ceiling a) |> DInt |> Value
        | _ -> incorrectArgs ())
      sqlSpec = NotYetImplementedTODO
      previewable = Pure
      deprecated = NotDeprecated }
    { name = fn "Float" "floor" 0
      parameters = [ Param.make "a" TFloat "" ]
      returnType = TInt
      description =
        "Round down to an integer value. Consider Float::truncate if your goal is to discard the fractional part of a number: `Float::floor -1.9 == -2.0` but `Float::truncate -1.9 == -1.0`."
      fn =
        (function
        | _, [ DFloat a ] -> bigint (Math.Floor a) |> DInt |> Value
        | _ -> incorrectArgs ())
      sqlSpec = NotYetImplementedTODO
      previewable = Pure
      deprecated = NotDeprecated }
    { name = fn "Float" "roundDown" 0
      parameters = [ Param.make "a" TFloat "" ]
      returnType = TInt
      description =
        "Round down to an integer value. Consider Float::truncate if your goal is to discard the fractional part of a number: `Float::floor -1.9 == -2.0` but `Float::truncate -1.9 == -1.0`."
      fn =
        (function
        | _, [ DFloat a ] -> bigint (Math.Floor a) |> DInt |> Value
        | _ -> incorrectArgs ())
      sqlSpec = NotYetImplementedTODO
      previewable = Pure
      deprecated = NotDeprecated }
    { name = fn "Float" "round" 0
      parameters = [ Param.make "a" TFloat "" ]
      returnType = TInt
      description = "Round to the nearest integer value"
      fn =
        (function
        | _, [ DFloat a ] -> bigint (Math.Round a) |> DInt |> Value
        | _ -> incorrectArgs ())
      sqlSpec = NotYetImplementedTODO
      previewable = Pure
      deprecated = NotDeprecated }
    { name = fn "Float" "truncate" 0
      parameters = [ Param.make "a" TFloat "" ]
      returnType = TInt
      description =
        "Discard the fractional portion of the float, rounding towards zero."
      fn =
        (function
        | _, [ DFloat a ] -> bigint (Math.Truncate a) |> DInt |> Value
        | _ -> incorrectArgs ())
      sqlSpec = NotYetImplementedTODO
      previewable = Pure
      deprecated = NotDeprecated }
    { name = fn "Float" "absoluteValue" 0
      parameters = [ Param.make "a" TFloat "" ]
      returnType = TFloat
      description =
        "Returns the absolute value of `a` (turning negative inputs into positive outputs)."
      fn =
        (function
        | _, [ DFloat a ] -> DFloat(Math.Abs a) |> Value
        | _ -> incorrectArgs ())
      sqlSpec = NotYetImplementedTODO
      previewable = Pure
      deprecated = NotDeprecated }
    { name = fn "Float" "negate" 0
      parameters = [ Param.make "a" TFloat "" ]
      returnType = TFloat
      description = "Returns the negation of `a`, `-a`."
      fn =
        (function
        | _, [ DFloat a ] -> DFloat(a * -1.0) |> Value
        | _ -> incorrectArgs ())
      sqlSpec = NotYetImplementedTODO
      previewable = Pure
      deprecated = NotDeprecated }
    { name = fn "Float" "sqrt" 0
      parameters = [ Param.make "a" TFloat "" ]
      returnType = TFloat
      description = "Get the square root of a float"
      fn =
        (function
        | _, [ DFloat a ] -> Value(DFloat(Math.Sqrt a))
        | _ -> incorrectArgs ())
      sqlSpec = NotYetImplementedTODO
      previewable = Pure
      deprecated = NotDeprecated }
    { name = fn "Float" "power" 0
      parameters = [ Param.make "base" TFloat ""; Param.make "exponent" TFloat "" ]
      returnType = TFloat
      description = "Returns `base` raised to the power of `exponent`"
      fn =
        (function
        | _, [ DFloat base_; DFloat exp ] -> Value(DFloat(base_ ** exp))
        | _ -> incorrectArgs ())
      sqlSpec = SqlBinOp "^"
      previewable = Pure
      deprecated = NotDeprecated }
    { name = fn "Float" "divide" 0
      parameters = [ Param.make "a" TFloat ""; Param.make "b" TFloat "" ]
      returnType = TFloat
      description = "Divide float `a` by float `b`"
      fn =
        (function
        | _, [ DFloat a; DFloat b ] -> Value(DFloat(a / b))
        | _ -> incorrectArgs ())
      sqlSpec = SqlBinOp "/"
      previewable = Pure
      deprecated = NotDeprecated }
    { name = fn "Float" "add" 0
      parameters = [ Param.make "a" TFloat ""; Param.make "b" TFloat "" ]
      returnType = TFloat
      description = "Add float `a` to float `b`"
      fn =
        (function
        | _, [ DFloat a; DFloat b ] -> Value(DFloat(a + b))
        | _ -> incorrectArgs ())
      sqlSpec = SqlBinOp "+"
      previewable = Pure
      deprecated = NotDeprecated }
    { name = fn "Float" "multiply" 0
      parameters = [ Param.make "a" TFloat ""; Param.make "b" TFloat "" ]
      returnType = TFloat
      description = "Multiply float `a` by float `b`"
      fn =
        (function
        | _, [ DFloat a; DFloat b ] -> Value(DFloat(a * b))
        | _ -> incorrectArgs ())
      sqlSpec = SqlBinOp "*"
      previewable = Pure
      deprecated = NotDeprecated }
    { name = fn "Float" "subtract" 0
      parameters = [ Param.make "a" TFloat ""; Param.make "b" TFloat "" ]
      returnType = TFloat
      description = "Subtract float `b` from float `a`"
      fn =
        (function
        | _, [ DFloat a; DFloat b ] -> Value(DFloat(a - b))
        | _ -> incorrectArgs ())
      sqlSpec = SqlBinOp "-"
      previewable = Pure
      deprecated = NotDeprecated }
    { name = fn "Float" "greaterThan" 0
      parameters = [ Param.make "a" TFloat ""; Param.make "b" TFloat "" ]
      returnType = TBool
      description = "Returns true if a is greater than b"
      fn =
        (function
        | _, [ DFloat a; DFloat b ] -> Value(DBool(a > b))
        | _ -> incorrectArgs ())
      sqlSpec = SqlBinOp ">"
      previewable = Pure
      deprecated = NotDeprecated }
    { name = fn "Float" "greaterThanOrEqualTo" 0
      parameters = [ Param.make "a" TFloat ""; Param.make "b" TFloat "" ]
      returnType = TBool
      description = "Returns true if a is greater than b"
      fn =
        (function
        | _, [ DFloat a; DFloat b ] -> Value(DBool(a >= b))
        | _ -> incorrectArgs ())
      sqlSpec = SqlBinOp ">="
      previewable = Pure
      deprecated = NotDeprecated }
    { name = fn "Float" "lessThan" 0
      parameters = [ Param.make "a" TFloat ""; Param.make "b" TFloat "" ]
      returnType = TBool
      description = "Returns true if a is less than b"
      fn =
        (function
        | _, [ DFloat a; DFloat b ] -> Value(DBool(a < b))
        | _ -> incorrectArgs ())
      sqlSpec = SqlBinOp "<"
      previewable = Pure
      deprecated = NotDeprecated }
    { name = fn "Float" "lessThanOrEqualTo" 0
      parameters = [ Param.make "a" TFloat ""; Param.make "b" TFloat "" ]
      returnType = TBool
      description = "Returns true if a is less than b"
      fn =
        (function
        | _, [ DFloat a; DFloat b ] -> Value(DBool(a <= b))
        | _ -> incorrectArgs ())
      sqlSpec = SqlBinOp "<="
      previewable = Pure
      deprecated = NotDeprecated }
    { name = fn "Float" "sum" 0
      parameters = [ Param.make "a" (TList TFloat) "" ]
      returnType = TFloat
      description = "Returns the sum of all the floats in the list"
      fn =
        (function
        | _, [ DList l as ldv ] ->
          let floats =
            List.map
              (fun f ->
                match f with
                | DFloat ft -> ft
                | t -> Errors.throw (Errors.argumentWasnt "a list of floats" "a" ldv))
              l

          let sum = List.fold (fun acc elem -> acc + elem) 0.0 floats
          Value(DFloat sum)
        | _ -> incorrectArgs ())
      sqlSpec = NotYetImplementedTODO
      previewable = Pure
      deprecated = NotDeprecated }
    { name = fn "Float" "min" 0
      parameters = [ Param.make "a" TFloat ""; Param.make "b" TFloat "" ]
      returnType = TFloat
      description = "Returns the lesser of float `a` and float `b`"
      fn =
        (function
        | _, [ DFloat a; DFloat b ] -> Value(DFloat(Math.Min(a, b)))
        | _ -> incorrectArgs ())
      sqlSpec = NotYetImplementedTODO
      previewable = Pure
      deprecated = NotDeprecated }
    { name = fn "Float" "max" 0
      parameters = [ Param.make "a" TFloat ""; Param.make "b" TFloat "" ]
      returnType = TFloat
      description = "Returns the greater of float `a` and float `b`"
      fn =
        (function
        | _, [ DFloat a; DFloat b ] -> Value(DFloat(Math.Max(a, b)))
        | _ -> incorrectArgs ())
      sqlSpec = NotYetImplementedTODO
      previewable = Pure
      deprecated = NotDeprecated }
    { name = fn "Float" "clamp" 0
      parameters =
        [ Param.make "value" TFloat ""
          Param.make "limitA" TFloat ""
          Param.make "limitB" TFloat "" ]
      returnType = TFloat
      description =
        "If `value` is within the range given by `limitA` and `limitB`, returns `value`.
         If `value` is outside the range, returns `limitA` or `limitB`, whichever is closer to `value`.
         `limitA` and `limitB` can be provided in any order."
      fn =
        (function
        | _, [ DFloat v; DFloat a; DFloat b ] ->
          if System.Double.IsNaN a || System.Double.IsNaN b then
            Value(DError(SourceNone, "clamp requires arguments to be valid numbers"))
          else
            let min, max = if a < b then (a, b) else (b, a)
            Value(DFloat(Math.Clamp(v, min, max)))
        | _ -> incorrectArgs ())
      sqlSpec = NotYetImplementedTODO
      previewable = Pure
      deprecated = NotDeprecated }
    { name = fn "Float" "roundTowardsZero" 0
      parameters = [ Param.make "a" TFloat "" ]
      returnType = TInt
      description =
        "Discard the fractional portion of the float, rounding towards zero."
      fn =
        (function
        | _, [ DFloat a ] -> bigint (Math.Truncate a) |> DInt |> Value
        | _ -> incorrectArgs ())
      sqlSpec = NotYetImplementedTODO
      previewable = Pure
      deprecated = NotDeprecated } ]
