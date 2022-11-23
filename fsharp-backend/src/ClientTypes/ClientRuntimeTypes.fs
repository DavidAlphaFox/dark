/// Runtime Types used for client-server communication so we may update backend
/// types without affecting APIs.
///
/// These should all directly match `RuntimeTypes.res` in `client`.
/// See `RuntimeTypes.fs` for documentation of these types.
module ClientTypes.Runtime

open Prelude
open Tablecloth


module FQFnName =
  type UserFnName = string

  type PackageFnName =
    { owner : string
      package : string
      module_ : string
      function_ : string
      version : int }

  type StdlibFnName = { module_ : string; function_ : string; version : int }

  type T =
    | User of UserFnName
    | Stdlib of StdlibFnName
    | Package of PackageFnName


type DType =
  | TInt
  | TFloat
  | TBool
  | TNull
  | TStr
  | TList of DType
  | TTuple of DType * DType * List<DType>
  | TDict of DType
  | TIncomplete
  | TError
  | THttpResponse of DType
  | TDB of DType
  | TDate
  | TChar
  | TPassword
  | TUuid
  | TOption of DType
  | TErrorRail
  | TUserType of string * int
  | TBytes
  | TResult of DType * DType
  | TVariable of string
  | TFn of List<DType> * DType
  | TRecord of List<string * DType>

type Pattern =
  | PVariable of id * string
  | PConstructor of id * string * List<Pattern>
  | PInteger of id * int64
  | PBool of id * bool
  | PCharacter of id * string
  | PString of id * string
  | PFloat of id * double
  | PNull of id
  | PBlank of id
  | PTuple of id * Pattern * Pattern * List<Pattern>

module Expr =
  type T =
    | EInteger of id * int64
    | EBool of id * bool
    | EString of id * string
    | ECharacter of id * string
    | EFloat of id * double
    | ENull of id
    | EBlank of id
    | ELet of id * string * T * T
    | EIf of id * T * T * T
    | ELambda of id * List<id * string> * T
    | EFieldAccess of id * T * string
    | EVariable of id * string
    | EApply of id * T * List<T> * IsInPipe * SendToRail
    | EFQFnValue of id * FQFnName.T
    | EList of id * List<T>
    | ETuple of id * T * T * List<T>
    | ERecord of id * List<string * T>
    | EConstructor of id * string * List<T>
    | EMatch of id * T * List<Pattern * T>
    | EFeatureFlag of id * T * T * T

  and SendToRail =
    | Rail
    | NoRail

  and IsInPipe =
    | InPipe of id
    | NotInPipe


module Dval =
  type DvalSource =
    | SourceNone
    | SourceID of tlid * id

  and Symtable = Map<string, T>

  and LambdaImpl =
    { parameters : List<id * string>
      symtable : Symtable
      body : Expr.T }

  and FnValImpl =
    | Lambda of LambdaImpl
    | FnName of FQFnName.T

  and DHTTP =
    | Redirect of string
    | Response of int64 * List<string * string> * T

  and T =
    | DInt of int64
    | DFloat of double
    | DBool of bool
    | DNull
    | DStr of string
    | DChar of string
    | DList of List<T>
    | DTuple of T * T * List<T>
    | DFnVal of FnValImpl // See docs/dblock-serialization.md
    | DObj of Map<string, T>
    | DError of DvalSource * string
    | DIncomplete of DvalSource
    | DErrorRail of T
    | DHttpResponse of DHTTP
    | DDB of string
    | DDate of NodaTime.LocalDateTime
    | DPassword of Password
    | DUuid of System.Guid
    | DOption of Option<T>
    | DResult of Result<T, T>
    | DBytes of byte array
