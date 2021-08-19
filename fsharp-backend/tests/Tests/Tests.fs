module Tests.All

// Main entry point for tests being run

open Expecto
open System.Threading.Tasks

[<EntryPoint>]
let main (args : string array) : int =
  LibRealExecution.RealExecution.init "Tests" // Must go before Tests.BwdServer.init
  let cancelationTokenSource = new System.Threading.CancellationTokenSource()
  let bwdServerTestsTask = Tests.BwdServer.init cancelationTokenSource.Token
  // CLEANUP For now, migrations are run by the ocaml process in run-fsharp-tests
  // LibBackend.Migrations.init ()
  let httpClientTestsTask = Tests.HttpClient.init cancelationTokenSource.Token
  LibService.Telemetry.Console.loadTelemetry ()
  (LibBackend.Account.initTestAccounts ()).Wait()

  let tests =
    [ Tests.Account.tests
      Tests.ApiServer.tests
      Tests.Authorization.tests
      Tests.BwdServer.tests
      Tests.Canvas.tests
      Tests.Cron.tests
      Tests.DvalRepr.tests
      Tests.EventQueue.tests
      Tests.Execution.tests
      Tests.FSharpToExpr.tests
      Tests.LibExecution.tests.Force()
      Tests.HttpClient.tests
      Tests.OCamlInterop.tests
      Tests.Prelude.tests
      Tests.ProgramTypes.tests
      Tests.Routing.tests
      Tests.StdLib.tests
      Tests.SqlCompiler.tests
      Tests.Traces.tests
      Tests.TypeChecker.tests
      Tests.Undo.tests
      Tests.UserDB.tests ]

  // this does async stuff within it, so do not run it from a task/async
  // context or it may hang
  let exitCode = runTestsWithCLIArgs [] args (testList "tests" tests)

  cancelationTokenSource.Cancel()
  bwdServerTestsTask.Wait()
  httpClientTestsTask.Wait()
  exitCode
