namespace SmsServer.Droid

open System
open System.IO
open System.Threading
open System.Threading.Tasks
open Android.App
open Android.Net
open Android.Net.Wifi
open Android.Content
open Android.Content.PM
open Android.Runtime
open Android.Views
open Android.Widget
open Android.OS
open Ionic
open Ionic.Zip
open Operators
open Newtonsoft.Json
open Newtonsoft.Json.Serialization
open Suave
open Suave.Filters
open Suave.Utils
open Suave.Writers
open Suave.Http
open Suave.Successful
open Suave.Response
open Suave.RequestErrors
open Suave.Operators
open Suave.Swagger.Rest
open Suave.Swagger.Serialization
open Suave.Swagger.FunnyDsl
open Suave.Swagger.Swagger
open SQLite

module WebServer =

  
  [<CLIMutable>]
  type SmsModel = {
    Id:int
    Address:string
    Body:string
    Date:DateTime
  }

  [<CLIMutable>]
  type SendSmsModel = {
    Destination:string
    Body:string
  }

  [<CLIMutable>]
  type SendSmsResult = {
    Success:bool
    Sms:SendSmsModel
  }

  let readAllRows (context:Context) uri =
    let cr = context.ContentResolver
    use c = cr.Query(Android.Net.Uri.Parse uri, null, null, null, null)
    let names = c.GetColumnNames()
    c.MoveToFirst() |> ignore
    seq {
      for _ in 0 .. c.Count-1 do
        let row = 
          names 
          |> Seq.map (fun name -> name, c.GetString(c.GetColumnIndex name))
          |> dict
        yield row
        c.MoveToNext() |> ignore
      c.Close()
    } |> Seq.toArray
  
  let getCanonicalAddresses context =
    readAllRows context "content://mms-sms/canonical-addresses"

  let getContacts context =
    let uri = Android.Provider.ContactsContract.CommonDataKinds.Phone.ContentUri.ToString()
    System.Diagnostics.Debug.WriteLine("uri: " + uri)
    readAllRows context uri

  let listSms (context:Context) uri skip limit =
    let cr = context.ContentResolver
    use c = cr.Query(Android.Net.Uri.Parse(uri), null, null, null, null)
    let names = c.GetColumnNames()
    c.MoveToFirst() |> ignore
    if skip > 0
    then c.MoveToPosition skip |> ignore
    seq {
      for i in 0 .. limit-1 do
        if c.Count > i + skip
        then
          let row = names |> Seq.map (fun name -> name, c.GetString(c.GetColumnIndex name)) |> dict
          yield row
        c.MoveToNext() |> ignore
      c.Close()
    } |> Seq.toList

  let listSentSms context skip limit =
    match skip with
    | Some n -> listSms context "content://sms/sent" n limit
    | None -> listSms context "content://sms/sent" 0 limit

  let listInboxSms context skip limit =
    match skip with
    | Some n -> listSms context "content://sms/inbox" n limit
    | None -> listSms context "content://sms/inbox" 0 limit

  let sentSmsWp context : WebPart = 
    (fun (x : HttpContext) ->
      async {
        let rs = JSON (listSentSms context None 20)
        return! rs x
      })

  let inboxSmsWp context : WebPart = 
   (fun (x : HttpContext) ->
     async {
       try
         let rs = JSON (listInboxSms context None 20)
         return! rs x
       with e -> 
         let rs = 
            OK (sprintf "error: %s" e.Message)
            >=> Writers.setStatus HttpCode.HTTP_500
         return! rs x
     })

  let run context (port:UInt16) =
    let manufacturer = Build.Manufacturer
    let model = Build.Model
    let deviceName = 
      if model.StartsWith(manufacturer)
      then model
      else manufacturer + " " + model
    
    let now : WebPart =
     fun (x : HttpContext) ->
       async {
         // The MODEL helper checks the "Accept" header 
         // and switches between XML and JSON format
         return! MODEL DateTime.Now x
       }

    let sendSms =
      JsonBody<SendSmsModel>(
        fun model -> 
          let success =
            try
              let manager = Android.Telephony.SmsManager.Default
              manager.SendTextMessage(model.Destination, null, model.Body, null, null)
              true
            with _ -> false
          MODEL { Success = success; Sms=model }
        )
    let folder = Environment.GetFolderPath Environment.SpecialFolder.Personal
    let swaggerPath = Path.Combine(folder, "swagger-ui")

    let canonicalAddresses : WebPart =
      fun (x : HttpContext) ->
        async {
          let r = getCanonicalAddresses context
          return! MODEL r x
        }
    let contacts : WebPart =
         fun (x : HttpContext) ->
           async {
             let r = getContacts context
             return! MODEL r x
           }
    let api = 
      swagger {
          for route in getting (simpleUrl "/contacts" |> thenReturns contacts) do
            yield description Of route is "contacts"

          for route in getting (simpleUrl "/canonicalAddresses" |> thenReturns canonicalAddresses) do
            yield description Of route is "canonical Addresses"

          for route in getting (simpleUrl "/time" |> thenReturns now) do
            yield description Of route is "What time is it ?"

          for route in getting (simpleUrl "/sms/sent" |> thenReturns (sentSmsWp context)) do
            yield description Of route is "Get last 20 sent SMS"
            yield route |> addResponse 200 "The found messages" (Some typeof<SmsModel>)

          for route in getOf (pathScan "/sms/sent/%d" 
                                (fun limit -> JSON (listSentSms context None limit))) do
            yield urlTemplate Of route is "/sms/sent/{count}"
            yield description Of route is "Get specified count of sent SMS"
            yield route |> addResponse 200 "The found messages" (Some typeof<SmsModel>)
            yield parameter "count" Of route (fun p -> { p with Type=Some(typeof<int32>); In=Path })

          for route in getOf 
                        (pathScan 
                           "/sms/sent/%d/%d" 
                           (fun (skip,limit) -> JSON (listSentSms context (Some skip) limit))) do
            yield urlTemplate Of route is "/sms/sent/{skip}/{count}"
            yield description Of route is "Skip specified count and get specified count of sent SMS"
            yield route |> addResponse 200 "The found messages" (Some typeof<SmsModel>)
            yield parameter "skip" Of route (fun p -> { p with Type=Some(typeof<int32>); In=Path })
            yield parameter "count" Of route (fun p -> { p with Type=Some(typeof<int32>); In=Path })

          for route in getting (simpleUrl "/sms/inbox" |> thenReturns (inboxSmsWp context)) do
            yield description Of route is "Get last 20 inbox SMS"
            yield route |> addResponse 200 "The found messages" (Some typeof<SmsModel>)

          for route in getOf (pathScan "/sms/inbox/%d" 
                                (fun limit -> JSON (listInboxSms context None limit))) do
            yield urlTemplate Of route is "/sms/inbox/{count}"
            yield description Of route is "Get specified count of inbox SMS"
            yield route |> addResponse 200 "The found messages" (Some typeof<SmsModel>)
            yield parameter "count" Of route (fun p -> { p with Type=Some(typeof<int32>); In=Path })

          for route in getOf (pathScan "/sms/inbox/%d/%d" 
                                (fun (skip,limit) -> JSON (listInboxSms context (Some skip) limit))) do
            yield urlTemplate Of route is "/sms/inbox/{skip}/{count}"
            yield description Of route is "Skip specified count and get specified count of inbox SMS"
            yield route |> addResponse 200 "The found messages" (Some typeof<SmsModel>)
            yield parameter "skip" Of route (fun p -> { p with Type=Some(typeof<int32>); In=Path })
            yield parameter "count" Of route (fun p -> { p with Type=Some(typeof<int32>); In=Path })

          for route in posting <| simpleUrl "/sms/send" |> thenReturns sendSms do
            yield description Of route is "Send a SMS"
            yield route |> addResponse 200 "returns the SMS sending result" (Some typeof<SendSmsResult>)
            yield parameter "sms" Of route (fun p -> { p with Type = Some(typeof<SendSmsModel>); In=Body })
    
      }
      |> fun a ->
          a.Describes(
            fun d -> 
              { 
                d with 
                    Title = "Sms server"
                    Description = "A small SMS REST API"
              })
    let mimeTypesMap = function
      | ".css" -> mkMimeType "text/css" false
      | ".gif" -> mkMimeType "image/gif" false
      | ".png" -> mkMimeType "image/png" false
      | ".htm"
      | ".html" -> mkMimeType "text/html" false
      | ".jpe"
      | ".jpeg"
      | ".jpg" -> mkMimeType "image/jpeg" false
      | ".js"  -> mkMimeType "application/x-javascript" false
      | a      -> defaultMimeTypesMap a
    
    let config = 
      { defaultConfig 
          with
            bindings              = [ HttpBinding.mk Protocol.HTTP Net.IPAddress.Any port]
            mimeTypesMap          = mimeTypesMap
            bufferSize            = 2048
            maxOps                = 200
            autoGrow              = true
            homeFolder            = None //Some swaggerPath
      }
    let serveSwagger p =
      let fullPath = Path.Combine(swaggerPath, p)
      if p = "index.html"
      then
        let r = System.IO.File.ReadAllText fullPath
        OK <| r.Replace("http://petstore.swagger.io/v2/swagger.json", "/swagger/v2/swagger.json")
      else
        Diagnostics.Debug.WriteLine (sprintf "serving %s in %s" p swaggerPath)
        Files.file fullPath
    
    let swaggerWebPart = 
      Suave.Filters.GET 
        >=> choose [
              pathScan "/swagger-ui/" (fun _ -> serveSwagger "index.html")
              pathScan "/swagger-ui/%s" serveSwagger
            ]
    let app = 
      choose [
        api.App
        swaggerWebPart
      ]

    let _, start = startWebServerAsync config app
    let cts = new CancellationTokenSource()
    let c = TaskCreationOptions()
    Async.StartAsTask(start,c,cts.Token) |> ignore
    cts

