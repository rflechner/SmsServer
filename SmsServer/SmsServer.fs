namespace SmsServer

open System
open Xamarin.Forms

module Operators =
  let (==>) (e:IEvent<_,'a>) (f:'a->unit) =
    e.Add f

type IAppInteractions =
  abstract member OnStartServerClick : Button -> ProgressBar -> UInt16 -> unit

open Operators

type App(interaction:#IAppInteractions) = 
  inherit Application()
  let stack = StackLayout(VerticalOptions = LayoutOptions.Start)
  let label1 = Label(XAlign = TextAlignment.Center, Text = "SMS REST Server")
  let label2 = Label(XAlign = TextAlignment.Start , Text = "Server port:")
  let portEntry = Entry(Text="8083")

  let startServer = Button(Text = "Start server")
  let progressBar = ProgressBar()

  let swaggerButton = Button(Text = "Open Swagger documentation")

  let getPort() =
    if String.IsNullOrWhiteSpace portEntry.Text
    then portEntry.Text <- "8083"
    UInt16.Parse portEntry.Text

  do
    startServer.Clicked
      ==> fun _ -> 
            getPort() |> interaction.OnStartServerClick startServer progressBar
    portEntry.TextChanged
      ==> fun a -> 
            let isNotValid = 
              portEntry.Text.ToCharArray()
                |> Array.exists (Char.IsDigit >> not)
            if isNotValid
            then portEntry.Text <- a.OldTextValue

    swaggerButton.Clicked
      ==> fun _ ->
            Device.OpenUri(Uri <| sprintf "http://localhost:%d/swagger-ui/index.html" (getPort()))

    progressBar.IsVisible <- false

    stack.Children.Add label1
    stack.Children.Add label2
    stack.Children.Add portEntry

    stack.Children.Add startServer
    stack.Children.Add progressBar

    stack.Children.Add swaggerButton

    base.MainPage <- ContentPage(Content = stack)


