/// Thin, bytes-friendly middleware
///
/// This Middleware corresponds with `HTTPBasic` handlers
[<RequireQualifiedAccess>]
module HttpMiddleware.HttpMiddlewareV1

open Prelude
open LibExecution.VendoredTablecloth

module RT = LibExecution.RuntimeTypes
module Telemetry = LibService.Telemetry

let lowercaseHeaderKeys (headers : HttpHeaders.T) =
  headers |> List.map (fun (k, v) -> (String.toLowercase k, v))

module Request =
  let fromRequest
    (uri : string)
    (headers : HttpHeaders.T)
    (query : string)
    (body : byte array)
    : RT.Dval =
    let headers =
      headers
      |> lowercaseHeaderKeys
      |> List.map (fun (k, v) -> RT.DTuple(RT.DStr(k), RT.DStr(v), []))
      |> RT.DList

    [ "body", RT.DBytes body
      "queryParams", RT.DStr query
      "headers", headers
      "url", RT.DStr uri ]
    |> RT.Dval.obj


module Response =
  type HttpResponse =
    { statusCode : int
      body : byte array
      headers : HttpHeaders.T }

  let toHttpResponse (result : RT.Dval) : HttpResponse =
    match result with
    // Expected user responses
    | RT.DHttpResponse (RT.Redirect str) ->
      Telemetry.addTags [ "response-type", "httpResponse redirect" ]
      { statusCode = int 302; headers = [ "Location", str ]; body = [||] }

    | RT.DHttpResponse (RT.Response (code, headers, RT.DBytes body)) ->
      Telemetry.addTags [ "response-type", "httpResponse response" ]
      { statusCode = int code; headers = lowercaseHeaderKeys headers; body = body }

    // Error responses
    | uncaughtResult ->
      Telemetry.addTags [ "response-type", "error"; "result", uncaughtResult ]
      { statusCode = 500
        headers = [ "Content-Type", "text/plain; charset=utf-8" ]
        body =
          UTF8.toBytes
            "Application error: the executed code did not result in a redirect or bytes response." }
