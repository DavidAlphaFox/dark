module LibBackend.Account

// Functions related to Accounts/Users

open System.Threading.Tasks
open FSharp.Control.Tasks
open FSharpPlus
open Npgsql.FSharp
open Npgsql

open Prelude
open Tablecloth
open Db

// **********************
// Types
// **********************

type Account =
  { username : UserName.T
    password : Password.T
    email : string
    name : string }

type UserInfo =
  { username : UserName.T
    name : string
    admin : bool
    email : string
    id : UserID }

type UserInfoAndCreatedAt =
  { username : UserName.T
    name : string
    admin : bool
    email : string
    id : UserID
    createdAt : System.DateTime }

type Validate =
  | Validate
  | DontValidate

// **********************
// Special usernames
// **********************

let bannedUsernames : List<UserName.T> =
  // originally from https://ldpreload.com/blog/names-to-reserve
  // we allow www, because we have a canvas there
  [ "abuse"
    "admin"
    "administrator"
    "autoconfig"
    "broadcasthost"
    "ftp"
    "hostmaster"
    "imap"
    "info"
    "is"
    "isatap"
    "it"
    "localdomain"
    "localhost"
    "mail"
    "mailer-daemon"
    "marketing"
    "mis"
    "news"
    "nobody"
    "noc"
    "noreply"
    "no-reply"
    "pop"
    "pop3"
    "postmaster"
    "root"
    "sales"
    "security"
    "smtp"
    "ssladmin"
    "ssladministrator"
    "sslwebmaster"
    "support"
    "sysadmin"
    "usenet"
    "uucp"
    "webmaster"
    "wpad"
    // original to us from here
    "billing"
    "dev"

    // alpha, but not beta, because user beta already exists (with ownership
    // transferred to us)
    "alpha" ]
  |> List.map UserName.create



// **********************
// Adding
// **********************

let validateUserName (username : string) : Result<unit, string> =
  (* rules: no uppercase, ascii only, must start with letter, other letters can
   * be numbers or underscores. 3-20 characters. *)
  let reString = @"^[a-z][a-z0-9_]{2,19}$"

  if FsRegEx.isMatch reString username then
    Ok()
  else
    Error($"Invalid username '{username}', must match /{reString}/")


let validateEmail (email : string) : Result<unit, string> =
  (* just checking it's roughly the shape of an email *)
  let reString = "^.+@.+\\..+$"

  if FsRegEx.isMatch reString email then
    Ok()
  else
    Error($"Invalid email '{email}'")


let validateAccount (account : Account) : Result<unit, string> =
  validateUserName (toString account.username)
  |> Result.and_ (validateEmail account.email)

// Passwords set here are only valid locally, production uses auth0 to check
// access
let upsertAccount
  (admin : bool)
  (validate : Validate)
  (account : Account)
  : Task<Result<unit, string>> =
  task {
    // FSTODO - this used to be default true
    let result = if validate = Validate then validateAccount account else Ok() in

    match result with
    | Ok () ->
      return!
        Sql.query
          "INSERT INTO accounts
             (id, username, name, email, admin, password)
             VALUES
             (@id, @username, @name, @email, @admin, @password)
             ON CONFLICT (username)
             DO UPDATE SET name = EXCLUDED.name,
                           email = EXCLUDED.email,
                           password = EXCLUDED.password"
        |> Sql.parameters [ "id", Sql.uuid (System.Guid.NewGuid())
                            "username", Sql.string (toString account.username)
                            "admin", Sql.bool admin
                            "name", Sql.string account.name
                            "email", Sql.string account.email
                            ("password",
                             account.password |> Password.toString |> Sql.string) ]
        |> Sql.executeStatementAsync
        |> Task.map Ok
    | Error _ -> return result
  }

let upsertAdmin = upsertAccount true
let upsertNonAdmin = upsertAccount false

// **********************
// Querying
// **********************

let userIDForUserName (username : UserName.T) : Task<UserID> =
  if List.contains username bannedUsernames then
    failwith "Banned username"
  else
    Sql.query
      "SELECT id
       FROM accounts
       WHERE accounts.username = @username"
    |> Sql.parameters [ "username", Sql.string (toString username) ]
    |> Sql.executeRowAsync (fun read -> read.uuid "id")

let usernameForUserID (userID : UserID) : Task<Option<UserName.T>> =
  Sql.query
    "SELECT username
     FROM accounts
     WHERE accounts.id = @userid"
  |> Sql.parameters [ "userid", Sql.uuid userID ]
  |> Sql.executeRowOptionAsync
       (fun read -> read.string "username" |> UserName.create)

let getUser (username : UserName.T) : Task<Option<UserInfo>> =
  Sql.query
    "SELECT name, email, admin, id
     FROM accounts
     WHERE accounts.username = @username"
  |> Sql.parameters [ "username", Sql.string (toString username) ]
  |> Sql.executeRowOptionAsync
       (fun read ->
         { username = username
           name = read.string "name"
           email = read.string "email"
           admin = read.bool "admin"
           id = read.uuid "id" })

let getUserCreatedAt (username : UserName.T) : Task<System.DateTime> =
  Sql.query
    "SELECT created_at
     FROM accounts
     WHERE accounts.username = @username"
  |> Sql.parameters [ "username", Sql.string (toString username) ]
  |> Sql.executeRowAsync (fun read -> read.dateTime "created_at")

let getUserAndCreatedAtAndAnalyticsMetadata
  (username : UserName.T)
  : Task<Option<UserInfoAndCreatedAt * string>> =
  Sql.query
    "SELECT name, email, admin, created_at, id, segment_metadata
     FROM accounts
     WHERE accounts.username = @username"
  |> Sql.parameters [ "username", Sql.string (toString username) ]
  |> Sql.executeRowOptionAsync
       (fun read ->
         { username = username
           name = read.string "name"
           email = read.string "email"
           admin = read.bool "admin"
           id = read.uuid "id"
           createdAt = read.dateTime "created_at" },
         read.string "segment_metadata")

let getUserByEmail (email : string) : Task<Option<UserInfo>> =
  Sql.query
    "SELECT name, username, admin, id
     FROM accounts
     WHERE accounts.email = @email"
  |> Sql.parameters [ "email", Sql.string email ]
  |> Sql.executeRowOptionAsync
       (fun read ->
         { username = UserName.create (read.string "username")
           name = read.string "name"
           email = email
           admin = read.bool "admin"
           id = read.uuid "id" })

let getUsers : Task<List<UserName.T>> =
  Sql.query
    "SELECT username
     FROM accounts"
  |> Sql.executeAsync (fun read -> UserName.create (read.string "username"))

let isAdmin (username : UserName.T) : Task<bool> =
  Sql.query
    "SELECT TRUE
     FROM accounts
     WHERE accounts.username = @username
       AND admin = true"
  |> Sql.parameters [ "username", Sql.string (toString username) ]
  |> Sql.executeExistsAsync

let setAdmin (admin : bool) (username : UserName.T) : Task<unit> =
  Sql.query
    "UPDATE accounts
        SET admin = @admin where username = @username"
  |> Sql.parameters [ "admin", Sql.bool admin
                      "username", Sql.string (toString username) ]
  |> Sql.executeStatementAsync

// Returns None if no valid user, or Some username _from the db_ if valid.
// Note: the input username may also be an email address. We do this because
// users input data this way and it seems silly not to allow it.
//
// No need to detect which and SQL differently; no valid username contains a
// '@', and every valid email address does. [If you say 'uucp bang path', I
// will laugh and then tell you to give me a real email address.]
//
// This function was converted from OCaml. The OCaml Libsodium
// (https://github.com/ahrefs/ocaml-sodium/blob/master/lib/sodium.ml), the F#
// version is libsodium-net
// (https://github.com/tabrath/libsodium-core/blob/master/src/Sodium.Core/PasswordHash.cs).
// The OCaml version uses the argon2i versions under the hood, which we use explicitly in F#.
let authenticate
  (usernameOrEmail : string)
  (givenPassword : string)
  : Task<Option<string>> =
  Sql.query
    "SELECT username, password from accounts
      WHERE accounts.username = @usernameOrEmail OR accounts.email = @usernameOrEmail"
  |> Sql.parameters [ "usernameOrEmail", Sql.string usernameOrEmail ]
  |> Sql.executeRowOptionAsync
       (fun read -> (read.string "username", read.string "password"))
  |> Task.map (
    Option.andThen
      (fun (username, password) ->
        let dbHash = password |> base64Decode |> ofBytes

        if Sodium.PasswordHash.ArgonHashStringVerify(dbHash, givenPassword) then
          Some(username)
        else
          None)
  )

let canAccessOperations (username : UserName.T) : Task<bool> = isAdmin username

// formerly called auth_domain_for
let ownerNameFromCanvasName (host : CanvasName.T) : OwnerName.T =
  match String.split "-" (toString host) with
  | owner :: _ -> OwnerName.create owner
  | _ -> OwnerName.create (toString host)

// **********************
// What user has access to
// **********************

let ownedCanvases (userID : UserID) : Task<List<CanvasName.T>> =
  Sql.query
    "SELECT DISTINCT c.name
     FROM canvases c
     WHERE c.account_id = @userID"
  |> Sql.parameters [ "userID", Sql.uuid userID ]
  |> Sql.executeAsync (fun read -> read.string "name" |> CanvasName.create)


// NB: this returns canvases an account has access to via an organization, not
// the organization(s) themselves
let accessibleCanvases (userID : UserID) : Task<List<CanvasName.T>> =
  Sql.query
    "SELECT c.name
       FROM access
      INNER JOIN accounts as org on access.organization_account = org.id
      INNER JOIN canvases as c on org.id = account_id
      WHERE access.access_account = @userID"
  |> Sql.parameters [ "userID", Sql.uuid userID ]
  |> Sql.executeAsync (fun read -> read.string "name" |> CanvasName.create)

let orgs (userID : UserID) : Task<List<OrgName.T>> =
  Sql.query
    "SELECT org.username
     FROM access
     INNER JOIN accounts as org on access.organization_account = org.id
     WHERE access.access_account = @userID"
  |> Sql.parameters [ "userID", Sql.uuid userID ]
  |> Sql.executeAsync (fun read -> read.string "name" |> OrgName.create)


// **********************
// Local/test developement
// **********************

let initTestAccounts () : Task<unit> =
  task {
    let! test_unhashed =
      upsertNonAdmin
        Validate
        { username = UserName.create "test_unhashed"
          password = Password.fromHash "fVm2CUePzGKCwoEQQdNJktUQ"
          email = "test+unhashed@darklang.com"
          name = "Dark OCaml Tests with Unhashed Password" }

    Result.unwrapUnsafe test_unhashed

    let! test =
      upsertNonAdmin
        Validate
        { username = UserName.create "test"
          password = Password.fromPlaintext "fVm2CUePzGKCwoEQQdNJktUQ"
          email = "test@darklang.com"
          name = "Dark OCaml Tests" }

    Result.unwrapUnsafe test

    let! test_admin =
      upsertAdmin
        Validate
        { username = UserName.create "test_admin"
          password = Password.fromPlaintext "fVm2CUePzGKCwoEQQdNJktUQ"
          email = "test+admin@darklang.com"
          name = "Dark OCaml Test Admin" }

    Result.unwrapUnsafe test_admin

    return ()
  }

let initBannedAccounts () : Task<unit> =
  task {
    do!
      bannedUsernames
      |> Task.iterSequentially
           (fun username ->
             task {
               let! result =
                 upsertNonAdmin
                   Validate
                   { username = username
                     password = Password.invalid
                     email = $"ops+{username}@darklang.com"
                     name = $"Disallowed account {username}" }

               return Result.unwrapUnsafe result
             })

    return ()
  }

let initAdmins () : Task<unit> =
  task {
    let password =
      Password.fromHash
        "JGFyZ29uMmkkdj0xOSRtPTMyNzY4LHQ9NCxwPTEkcEQxWXBLOG1aVStnUUJUYXdKZytkQSR3TWFXb1hHOER1UzVGd2NDYzRXQVc3RlZGN0VYdVpnMndvZEJ0QnY1bkdJAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="

    let! darkUser =
      upsertAdmin
        Validate
        { username = UserName.create "dark"
          password = password
          email = "ops+darkuser@darklang.com"
          name = "Dark Local Admin user" }

    Result.unwrapUnsafe darkUser

    let! paulUser =
      upsertAdmin
        Validate
        { username = UserName.create "paul"
          password = password
          email = "paul@darklang.com"
          name = "Paul Biggar" }

    Result.unwrapUnsafe paulUser
    return ()
  }

// accounts to create namespaces for dark canvases
let initUsefulCanvases () : Task<unit> =
  // Needed for tests
  task {
    let! darkUser =
      upsertNonAdmin
        Validate
        { username = UserName.create "sample"
          password = Password.invalid
          email = "opsample@darklang.com"
          name = "Sample owner" }

    Result.unwrapUnsafe darkUser

    return ()
  }


let init () : Task<unit> =
  task {
    if Config.createAccounts then
      do! initTestAccounts ()
      do! initBannedAccounts ()
      do! initAdmins ()
      do! initUsefulCanvases ()
      return ()
    else
      return ()
  }
