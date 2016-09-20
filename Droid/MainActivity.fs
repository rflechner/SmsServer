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
open Android.Telephony
open Android.Hardware.Usb
open Ionic
open Ionic.Zip
open Operators

// https://forums.xamarin.com/discussion/11889/sending-sms-and-display-toast-when-message-is-sent-and-or-delivered
// http://stackoverflow.com/questions/33919185/detecting-moving-and-reading-a-sms-android-c-sharp

[<BroadcastReceiver(Enabled = true, Label = "SMS Receiver", Exported=true)>]
[<IntentFilter([|"android.provider.Telephony.SMS_RECEIVED"|])>]
type MySMSReceiver () =
  inherit BroadcastReceiver()

  override __.OnReceive(context:Context, intent:Intent) =
    let pdus = (intent.Extras.Get "pdus").ToArray<Java.Lang.Object>()
    let format = (intent.Extras.GetString "format")
    let messages =
      seq {
        for i in 0..pdus.Length-1 do
          let p = pdus.[i]
          let bin = p.ToArray() |> Seq.cast<byte> |> Seq.toArray
          yield SmsMessage.CreateFromPdu(bin,format)
      } |> Seq.toList
    
    for m in messages do
      let message = "sms: " + m.MessageBody
      Diagnostics.Debug.WriteLine(message)
      //let toast = Toast.MakeText(context, message, ToastLength.Long)
      //toast.Show()
    ()

[<Service>]
[<IntentFilter([|"continuum.sms.smsService"; "android.intent.action.BOOT_COMPLETED"|])>]
type SmsService () =
  inherit Service()

  override __.OnCreate() =
    let receiver = new MySMSReceiver()
    let filter = new IntentFilter("android.provider.Telephony.SMS_RECEIVED")
    __.RegisterReceiver(receiver, filter) |> ignore
  override __.OnBind intent =
    null
  

[<Activity (Label = "SmsServer.Droid", Icon = "@drawable/icon", MainLauncher = true, ConfigurationChanges = (ConfigChanges.ScreenSize ||| ConfigChanges.Orientation))>]
type MainActivity() =
    inherit Xamarin.Forms.Platform.Android.FormsApplicationActivity()

    member __.UnzipSwagger (button:Xamarin.Forms.Button) (progressBar:Xamarin.Forms.ProgressBar) =
      progressBar.IsVisible <- true
      progressBar.Progress <- 0.
      button.IsVisible <- false
      async {
        do! Async.Sleep 1000
        let assembly = System.Reflection.Assembly.GetExecutingAssembly()
        use fs = assembly.GetManifestResourceStream "swagger-ui.zip"
        let folder = Environment.GetFolderPath Environment.SpecialFolder.Personal
        let zipPath = Path.Combine(folder, "swagger-ui.zip")
        if File.Exists zipPath |> not
        then 
          use output = File.OpenWrite(zipPath)
          fs.CopyTo output
          fs.Flush()
        let swaggerPath = Path.Combine(folder, "swagger-ui")
        let zip = new ZipInputStream(fs)
        use file = new Ionic.Zip.ZipFile (zipPath)
        let lastProgress = ref 0.
        file.ExtractProgress.Add(
          fun arg -> 
            __.RunOnUiThread(
              fun _ ->
                let percent = (float arg.EntriesExtracted) / (float arg.EntriesTotal)
                if percent - !lastProgress > 5.
                then 
                  progressBar.Progress <- percent
                  lastProgress := percent
            ))
        try
          file.ExtractAll(swaggerPath, ExtractExistingFileAction.DoNotOverwrite)
          do! Async.Sleep 1000
        with e -> 
          Diagnostics.Debug.WriteLine e.Message

        __.RunOnUiThread(fun _ ->
          Diagnostics.Debug.WriteLine "extract is over"
          progressBar.IsVisible <- false
          button.IsVisible <- true
        )
      } |> Async.StartAsTask |> Async.AwaitTask
  
    member val sTask:CancellationTokenSource option = None with get, set

    interface SmsServer.IAppInteractions with
      member __.OnStartServerClick button progressBar port =
        match __.sTask with
        | Some t ->
            t.Cancel()
            __.sTask <- None
            button.Text <- "Start server"
        | None -> 
            __.UnzipSwagger button progressBar |> ignore
            try
              let token = WebServer.run __.BaseContext port
              __.sTask <- Some token
            with
            | e -> 
                Toast.MakeText(__.BaseContext, e.Message, ToastLength.Long).Show()
            button.Text <- "Stop server"

            let wifiMgr = __.GetSystemService(Context.WifiService) :?> WifiManager
            let wifiInfo = wifiMgr.ConnectionInfo
            let ipv = int64 wifiInfo.IpAddress
            if ipv > 0L
            then
              let bint = Java.Math.BigInteger.ValueOf ipv
              let bytes = bint.ToByteArray() |> Array.rev
              let address = Java.Net.InetAddress.GetByAddress(bytes)
              let ip = address.HostAddress
              let message = sprintf "IP: %s" ip
              let toast = Toast.MakeText(__.BaseContext, message, ToastLength.Long)
              toast.Show()

    override this.OnCreate (bundle: Bundle) =
      base.OnCreate (bundle)

      Xamarin.Forms.Forms.Init (this, bundle)

      this.StartService(new Intent("continuum.sms.smsService")) |> ignore

      this.LoadApplication (new SmsServer.App (this))

      //WebServer.run this.BaseContext (uint16 8083) |> ignore

