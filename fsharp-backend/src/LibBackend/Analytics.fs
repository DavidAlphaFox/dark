module LibBackend.Analytics

// Analytics functions for users

module Account = LibBackend.Account

open System.Threading.Tasks
open FSharp.Control.Tasks

open Npgsql.FSharp
open Npgsql
open LibBackend.Db

open Prelude
open Tablecloth

module FireAndForget = LibService.FireAndForget


// We call this in two contexts: DarkInternal:: fns, and
// bin/heapio_identify_users.exe.
let identifyUser (executionID : ExecutionID) (username : UserName.T) : unit =
  FireAndForget.fireAndForgetTask "identify user" executionID (fun () ->
    task {
      // FSTODO: check that analytics_metadata has been set correctly
      let! data = Account.getUserAndCreatedAtAndAnalyticsMetadata username
      let (userInfoAndCreatedAt, heapioMetadata) = Option.unwrapUnsafe data
      let! _organization =
        task {
          let! orgs = Authorization.orgsFor username
          // A user's orgs for this purpose do not include orgs it has
          // read-only access to
          return
            orgs
            |> List.filter (function
              | _, rw -> rw = Authorization.ReadWrite)
            // If you have one org, that's your org! If you have no orgs, or
            // more than one, then we just use your username. This is because
            // Heap's properties/traits don't support lists.
            |> (function
            | [ (orgName, _) ] -> orgName
            | _ -> username |> string |> OrgName.create)
        }
      // let payload =
      // FSTODO
      // let payload =
      //   Assoc [ ("username", String user_info_and_created_at.username)
      //           ("email", String user_info_and_created_at.email)
      //           ("name", String user_info_and_created_at.name)
      //           ("admin", Bool user_info_and_created_at.admin)
      //           ("handle", String user_info_and_created_at.username)
      //           ("organization", String organization) ]
      // // We do zero checking of fields in heapio_metadata, but this is ok
      // // because it's a field we control, going to a service only we see.
      // // If we wanted to harden this later, we could List.filter the
      // // heapio_metadata yojson *)
      // Yojson.Safe.Util.combine payload heapio_metadata
      // FSTODO
      // do!
      //   LibService.HeapAnalytics.heapioEvent
      //     executionID
      //     userInfoAndCreatedAt.id
      //     Identify
      //     payload
      return ()
    })