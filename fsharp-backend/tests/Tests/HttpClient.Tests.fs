module Tests.HttpClient

open Expecto

open System.Threading.Tasks
open FSharp.Control.Tasks

open System.IO
open System.IO.Compression
open System.Text.RegularExpressions

type ConcurrentDictionary<'a, 'b> =
  System.Collections.Concurrent.ConcurrentDictionary<'a, 'b>

open Prelude
open Tablecloth

module RT = LibExecution.RuntimeTypes
module PT = LibExecution.ProgramTypes
module Exe = LibExecution.Execution

open TestUtils

type TestCase = { expected : Http.T; result : Http.T }

let testCases : ConcurrentDictionary<string, TestCase> = ConcurrentDictionary()

let host = $"test.builtwithdark.localhost:{TestConfig.httpClientPort}"

let normalizeHeaders
  (body : byte array)
  (headers : (string * string) list)
  : (string * string) list =
  headers
  |> List.map
       (function
       // make writing tests easier
       | (key, "HOST") when String.equalsCaseInsensitive "Host" key -> (key, host)
       // optionally change content length for writing responses more easily
       | (key, "LENGTH") when String.equalsCaseInsensitive "Content-length" key ->
         (key, string body.Length)
       | other -> other)

let randomBytes =
  [ 0x2euy; 0x0Auy; 0xE8uy; 0xE6uy; 0xF1uy; 0xE0uy; 0x9Buy; 0xA6uy; 0xEuy ]

// We can't add bytes to the test cases cause they're parsed a strings, so allow the
// string RANDOM_BYTES which will be replaced with the pre-selected bytes chosen at
// random.

let updateBody (body : byte array) : byte array =
  let rec find (bytes : byte list) : byte list =
    match bytes with
    | 0x52uy :: 0x41uy :: 0x4Euy :: 0x44uy :: 0x4Fuy :: 0x4Duy :: 0x5Fuy :: 0x42uy :: 0x59uy :: 0x54uy :: 0x45uy :: 0x53uy :: tail ->
      randomBytes @ find tail
    | [] -> []
    | head :: tail -> head :: find tail

  body |> List.fromArray |> find |> List.toArray






let t filename =
  // Parse the file contents now, rather than later, so that tests that refer to
  // other tests (that is, tests for redirects) will work.
  let skip = String.startsWith "_" filename
  let name = if skip then String.dropLeft 1 filename else filename

  let filename = $"tests/httpclienttestfiles/{filename}"
  let contents = System.IO.File.ReadAllBytes filename
  let content = UTF8.ofBytesUnsafe contents

  let expectedRequest, response, code =
    let m =
      Regex.Match(
        content,
        "^(\[expected-request\]\n(.*)\n)\[response\]\n(.*)\n\n\[test\]\n(.*)$",
        RegexOptions.Singleline
      )

    if not m.Success then failwith $"incorrect format in {name}"
    let g = m.Groups

    (g.[2].Value, g.[3].Value, g.[4].Value)

  let expected =
    expectedRequest |> UTF8.toBytes |> Http.setHeadersToCRLF |> Http.split
  let newExpectedBody = updateBody expected.body
  let expected =
    { expected with
        headers = normalizeHeaders newExpectedBody expected.headers
        body = newExpectedBody }
  let response = response |> UTF8.toBytes |> Http.setHeadersToCRLF |> Http.split
  let newResponseBody = updateBody response.body
  let response =
    { response with
        headers = normalizeHeaders newResponseBody response.headers
        body = newResponseBody }

  testCases.[name] <- { expected = expected; result = response }


  // Load the testcases first so that redirection works
  testTask $"HttpClient files: {filename}" {
    let testOCaml, testFSharp =
      if String.includes "FSHARPONLY" code then (false, true)
      else if String.includes "OCAMLONLY" code then (true, false)
      else (true, true)

    // debuG "expectedRequest" (toStr expectedRequest)
    // debuG "response" (toStr response)
    // debuG "code" code

    if skip then
      skiptest $"underscore test - {name}"
    else
      // Parse the code
      let shouldEqual, actualProg, expectedResult =
        code
        |> String.replace "URL" $"{host}/{name}"
        // CLEANUP: this doesn't use the correct length, as it might be latin1 or
        // compressed
        |> String.replace "LENGTH" (string response.body.Length)
        |> FSharpToExpr.parse
        |> FSharpToExpr.convertToTest

      let! state = executionStateFor "test-httpclient-${name}" Map.empty Map.empty

      let! expected =
        Exe.executeExpr state Map.empty (expectedResult.toRuntimeType ())

      let msg = $"\n\n{actualProg}\n=\n{expectedResult}\n\n"

      // Test OCaml
      if testOCaml then
        let! ocamlActual =
          try
            LibBackend.OCamlInterop.execute
              state.program.accountID
              state.program.canvasID
              actualProg
              Map.empty
              []
              []

          with
          | e -> failwith $"When calling OCaml code, OCaml server failed: {msg}, {e}"

        if shouldEqual then
          Expect.equalDval
            (normalizeDvalResult ocamlActual)
            expected
            $"{msg} -> OCaml"
        else
          Expect.notEqual
            (normalizeDvalResult ocamlActual)
            expected
            $"{msg} -> OCaml"

      // Test F#
      if testFSharp then
        let! fsharpActual =
          Exe.executeExpr state Map.empty (actualProg.toRuntimeType ())

        let fsharpActual = normalizeDvalResult fsharpActual

        if shouldEqual then
          Expect.equalDval fsharpActual expected $"{msg} -> FSharp"
        else
          Expect.notEqual fsharpActual expected $"{msg} -> FSharp"
  }


// ---------------
// This is the webserver that we will be testing against. It reads the testCases,
// checks that the request is expected, then returns the test case output.
// ---------------
open Microsoft.AspNetCore
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Http.Extensions
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Logging.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.DependencyInjection.Extensions

type ErrorResponse =
  { expectedStatus : string
    actualStatus : string
    expectedHeaders : List<string * string>
    actualHeaders : List<string * string>
    expectedBody : string
    actualBody : string
    message : string }

type Compression =
  | Deflate
  | Brotli
  | Gzip

let runTestHandler (ctx : HttpContext) : Task<HttpContext> =
  task {
    try
      let testName =
        let segment = System.Uri(ctx.Request.Path.Value).Segments.[1]
        if String.endsWith "/" segment then String.dropRight 1 segment else segment
      let testCase = testCases.[testName]

      let actualHeaders =
        BwdServer.Server.getHeaders ctx
        |> Map
        // .NET always adds a Content-Length header, but OCaml doesn't
        |> Map.remove "Content-Length"
      let! actualBody = BwdServer.Server.getBody ctx

      let actualStatus =
        $"{ctx.Request.Method} {ctx.Request.GetEncodedPathAndQuery()} {ctx.Request.Protocol}"

      let expectedHeaders = Map testCase.expected.headers
      let expectedBody = testCase.expected.body
      let expectedStatus =
        testCase.expected.status |> String.replace "PATH" ctx.Request.Path.Value

      if (actualStatus, actualHeaders, actualBody) = (expectedStatus,
                                                      expectedHeaders,
                                                      expectedBody) then
        let mutable compression = None
        let mutable transcodeToLatin1 = false

        ctx.Response.StatusCode <-
          testCase.result.status
          |> String.split " "
          |> List.getAt 1
          |> Option.unwrapUnsafe
          |> int
        List.iter
          (fun (k, v) ->
            if String.equalsCaseInsensitive k "Content-Encoding" then
              if v = "deflate" then compression <- Some Deflate
              else if v = "br" then compression <- Some Brotli
              else if v = "gzip" then compression <- Some Gzip
              else ()
            elif String.equalsCaseInsensitive k "Content-Type" then
              if v.Contains "charset=iso-8859-1"
                 || v.Contains "charset=latin1"
                 || v.Contains "us-ascii" then
                transcodeToLatin1 <- true

            BwdServer.Server.setHeader ctx k v)
          testCase.result.headers

        let data =
          if transcodeToLatin1 then
            System.Text.Encoding.Convert(
              System.Text.Encoding.UTF8,
              System.Text.Encoding.Latin1,
              testCase.result.body
            )
          else
            testCase.result.body

        match compression with
        | Some algo ->
          let stream : Stream =
            let body = ctx.Response.Body
            match algo with
            | Gzip -> new GZipStream(body, CompressionMode.Compress)
            | Brotli -> new BrotliStream(body, CompressionMode.Compress)
            | Deflate -> new DeflateStream(body, CompressionMode.Compress)
          do! stream.WriteAsync(data, 0, data.Length)
          do! stream.FlushAsync()
          do! stream.DisposeAsync()
        | None -> do! ctx.Response.Body.WriteAsync(data, 0, data.Length)
      else
        let expectedHeaders =
          expectedHeaders |> Map.toList |> List.sortBy Tuple2.first
        let actualHeaders = actualHeaders |> Map.toList |> List.sortBy Tuple2.first
        let message =
          [ (if actualStatus <> expectedStatus then "status" else "")
            (if actualHeaders <> expectedHeaders then "headers" else "")
            (if actualBody <> expectedBody then "body" else "") ]
          |> List.filter ((<>) "")
          |> String.concat ", "
          |> fun s -> $"The request to the server differs in {s}"

        let body =
          { message = message
            expectedStatus = expectedStatus
            actualStatus = actualStatus
            expectedHeaders = expectedHeaders
            expectedBody = UTF8.ofBytesUnsafe expectedBody
            actualHeaders = actualHeaders
            actualBody = UTF8.ofBytesUnsafe actualBody }
          |> Json.Vanilla.prettySerialize
          |> UTF8.toBytes

        ctx.Response.StatusCode <- 400
        ctx.Response.ContentLength <- int64 body.Length
        do! ctx.Response.Body.WriteAsync(body, 0, body.Length)

      return ctx
    with
    | e ->
      print $"Exception raised in test handler: {e}"
      failwith $"Exception raised in test handler: {e}"
      return ctx
  }


// Replace the console logger with one which writes to Prelude.NonBlockingConsole.
// Has the benefit that it speeds up tests by a factor of 3.
// https://docs.microsoft.com/en-us/dotnet/core/extensions/custom-logging-provider

type Logger() =
  interface ILogger with
    member this.Log<'TState>
      (
        level : LogLevel,
        eventID : EventId,
        state : 'TState,
        exc : exn,
        formatter : System.Func<'TState, exn, string>
      ) : unit =
      print ($"{eventID, 3} " + formatter.Invoke(state, exc))

    member _.IsEnabled(level : LogLevel) : bool = true

    member _.BeginScope<'TState>(state : 'TState) : System.IDisposable =
      Unchecked.defaultof<System.IDisposable>

type LoggerProvider() =
  interface ILoggerProvider with
    member this.CreateLogger(_categoryName : string) : ILogger = new Logger()

    member this.Dispose() : unit = ()

let configureLogging (builder : ILoggingBuilder) : unit =
  // This removes the default ConsoleLogger. Having two console loggers (this one and
  // also the one in Main), caused a deadlock (possibly from having two different
  // console logging threads).
  builder
    .ClearProviders()
    .Services
    .TryAddEnumerable(
      ServiceDescriptor.Singleton<ILoggerProvider, LoggerProvider>()
    )



let configureApp (app : IApplicationBuilder) =
  let handler (ctx : HttpContext) : Task = runTestHandler ctx
  app.Run(RequestDelegate handler)

let configureServices (services : IServiceCollection) : unit = ()


let webserver () =
  Host.CreateDefaultBuilder()
  |> fun h -> h.ConfigureLogging(configureLogging)
  |> fun h ->
       h.ConfigureWebHost
         (fun wh ->
           wh
           |> fun wh -> wh.UseKestrel()
           |> fun wh -> wh.UseUrls($"http://*:{TestConfig.httpClientPort}")
           |> fun wh -> wh.ConfigureServices(configureServices)
           |> fun wh -> wh.Configure(configureApp)
           |> ignore<IWebHostBuilder>)
  |> fun h -> h.Build()

// run a webserver to read test input
let init (token : System.Threading.CancellationToken) : Task =
  (webserver ()).RunAsync(token)


let testsFromFiles =
  let dir = "tests/httpclienttestfiles/"

  System.IO.Directory.GetFiles(dir, "*")
  |> Array.map (System.IO.Path.GetFileName)
  |> Array.toList
  |> List.filter ((<>) "README.md")
  |> List.map t

let tests = testList "HttpClient" [ testList "From files" testsFromFiles ]
