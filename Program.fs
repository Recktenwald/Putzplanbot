module Putzbot

open System
open System.IO
open System.Net
open Funogram.Api
open Funogram.Types
open Funogram.Telegram.Api
open Funogram.Telegram.Types
open Funogram.Telegram.Bot
open FSharp.Json

open Types



open System.Text.RegularExpressions

type SecretApi =
    { Token: string
      AllFlatmates: Flatmate list
      InitialPutzplan: Putzplan
      DefaultMainChat: int64 }

let apiProof =
    { Token = Secret.Token
      AllFlatmates = Secret.allFlatmates
      InitialPutzplan = Secret.initialPutzplan
      DefaultMainChat = Secret.defaultMainChat }


let (|Regex|_|) pattern input =
    let m = Regex.Match(input, pattern)

    if m.Success then
        Some(List.tail [ for g in m.Groups -> g.Value ])
    else
        None

// TODO Link the two Müll somehow
// TODO Get rid of the part where one has to say /hi. also figure out why we have to say it so often.

// #r "nuget: FSharp.Json"

// Boilerplate
[<Literal>]
let LogFileName = "./secret/log.txt"

type Log = { TimeStamp: DateTime; Text: string }

let mutable myLogs: Log list = []

let maxLogLength = 20



let log s =
    myLogs <-
        { TimeStamp = DateTime.Now; Text = s }
        :: if List.length myLogs >= maxLogLength then
               myLogs.[0..(maxLogLength - 2)]
           else
               myLogs

    File.WriteAllLines(
        LogFileName,
        [ for l in myLogs do
              l.TimeStamp.ToString() + "\n\t" + l.Text + "\n" ]
    )
// File.WriteAllLines(LogFileName, DateTime.Now.ToString() + "\t" + x + "\n")

let config =
    { defaultConfig with
          Token = Secret.Token
          OnError = (fun e -> log (sprintf "%A" e)) }

let processResultWithValue (result: Result<'a, ApiResponseError>) =
    match result with
    | Ok v -> Some v
    | Error e ->
        printfn "Server error: %s" e.Description
        None

let processResult (result: Result<'a, ApiResponseError>) = processResultWithValue result |> ignore

let botResult data =
    api config data |> Async.RunSynchronously

let bot data = botResult data |> processResult

// Boilerplate End

[<Literal>]
let PutzplanFileName = "./secret/putzplan.json"



// Basic Types and some convenience functions for them

let defaultNamingFun (x: Flatmate option) =
    match x with
    | None -> "Unassigned"
    | Some f -> f.Name

let flatmateFromUser: User -> Flatmate option =
    flatmateFromUserHelper Secret.allFlatmates



type Model =
    { Putzplan: Putzplan
      MainChat: ChatId }



let readPutzplan () : Model =
    File.ReadAllText PutzplanFileName
    |> Json.deserialize<Model>



let writeModel model =
    printfn "Attempting to write"
    File.WriteAllText(PutzplanFileName, model |> Json.serialize)




let mutable model =
    { Putzplan = Secret.initialPutzplan
      MainChat = ChatId.Int Secret.defaultMainChat }

let updateModelWith m =
    try
        model <- m
        printfn "updated model"
        File.WriteAllText(PutzplanFileName, model |> Json.serialize)
    with
    | ex -> printfn "When trying to update the model I encountered the following exception: %s" (ex.ToString())




// -------------------------------------------------







let makeKeyboard command putzplan =
    let tasks =
        let toButton (task: Todo) : KeyboardButton =
            { Text = command + " " + task.Name
              RequestContact = None
              RequestLocation = None }

        putzplan |> List.map (toButton)

    let rec toMatrix (mat: 'a list list) (values: 'a list) : 'a list list =
        match values with
        | [] -> mat
        | [ x ] -> [ x ] :: mat
        | _ -> toMatrix ([ values.[0]; values.[1] ] :: mat) (List.tail (List.tail values))

    toMatrix [] tasks
    |> List.map (Seq.ofList)
    |> Seq.ofList



let sendMsg id msg =
    sendMessageBase id msg (Some ParseMode.Markdown) None None None None

let sendMsgDelKb id msg =
    let markup =
        Markup.ReplyKeyboardRemove
            { RemoveKeyboard = true
              Selective = None }

    sendMessageBase id msg (Some ParseMode.Markdown) None None None (Some markup)






// refactor to an onMessage function
let onMessage context =

    let chatId = context.Update.Message.Value.Chat.Id

    let user =
        Option.bind flatmateFromUser context.Update.Message.Value.From


    let sendReply text =
        sendMessageBase
            (ChatId.Int chatId)
            text
            (Some ParseMode.HTML)
            None
            None
            (Some context.Update.Message.Value.MessageId)
            None

    let sendReplyDelKeyboard text =
        let markup =
            Markup.ReplyKeyboardRemove
                { RemoveKeyboard = true
                  Selective = None }

        sendMessageBase
            (ChatId.Int chatId)
            text
            (Some ParseMode.HTML)
            None
            None
            (Some context.Update.Message.Value.MessageId)
            (Some markup)

    let sendKeyboard text keyboard =
        sendMessageBase
            (ChatId.Int chatId)
            text
            (Some ParseMode.HTML)
            None
            None
            (Some context.Update.Message.Value.MessageId)
            (Some keyboard)



    let possibleFile = context.Update.Message.Value.Document

    if Option.isSome possibleFile then
        match botResult (getFile possibleFile.Value.FileId) with
        | Error e -> printfn "Server error: %s" e.Description
        | Ok v ->
            printfn "%A" v

            try
                let url =
                    "https://api.telegram.org/file/bot"
                    + Secret.Token
                    + "/"
                    + v.FilePath.Value

                let newModel =
                    let req = WebRequest.Create(Uri(url))
                    use resp = req.GetResponse()
                    use stream = resp.GetResponseStream()
                    use reader = new IO.StreamReader(stream)
                    reader.ReadToEnd() |> Json.deserialize<Model>

                // model <- newModel
                updateModelWith newModel
                sendReply "I updated the model" |> bot
            with
            | :? System.NullReferenceException ->
                sendReply "I couldn't find the file. That should not have happened, maybe try again?"
                |> bot
            | ex ->
                sendReply (
                    sprintf
                        "I couldn't parse the model. Or maybe something else went wrong. I got this exception: %s"
                        (ex.ToString())
                )
                |> bot


    else
        ()
    // this was for testing
    // let url =
    //     "https://api.telegram.org/file/bot"
    //     + Secret.Token
    //     + "/documents/file_0.json"

    // let fetchUrl callback url =
    //     let req = WebRequest.Create(Uri(url))
    //     use resp = req.GetResponse()
    //     use stream = resp.GetResponseStream()
    //     use reader = new IO.StreamReader(stream)
    //     callback reader url

    // let myCallback (reader: IO.StreamReader) url =
    //     let html = reader.ReadToEnd()
    //     let html1000 = html.Substring(0, 1000)
    //     printfn "Downloaded %s. First 1000 is %s" url html1000
    // // html // return all the html

    // let parseModel (reader: IO.StreamReader) url =
    //     reader.ReadToEnd() |> Json.deserialize<Model>

    // let x = fetchUrl parseModel url



    let result =
        processCommands
            context
            [ cmd
                "/help"
                (fun _ ->
                    let response =
                        """⭐️Available commands:
/ls - Show the Putzplan
/done - Gives you a keyboard of all your tasks, click the one you did
/done task - Marks the task as done. If it wasn't yours it will stay with the current person
/bump - Gives you a keyboard of all tasks, click the one you want to increase the priority of
/bump task - Increases the priority of the task and pings the responsible person in the main chat
/rmk - Should you be stuck with a custom keyboard, this will get rid of it. Should not be needed.
/showmewhatyouvegot - Gives you a json of the current internal model.
/remind - reminds everybody of all their **overdue!** tasks
                          """

                    bot <| sendReply response)
              cmd
                  "/showmewhatyouvegot"
                  (fun _ ->
                      (let stream1 =
                          new System.IO.FileStream(PutzplanFileName, System.IO.FileMode.Open)

                       let file: FileToSend =
                           FileToSend.File(PutzplanFileName, stream1)

                       do bot (sendDocument chatId file "Here is the current model")
                       do stream1.Close()))



              cmd
                  "/testmainchat"
                  (fun _ ->
                      (sendMsg model.MainChat "This is my main chat")
                      |> bot)
              cmd
                  "/mainchat"
                  (fun c ->
                      (updateModelWith
                          { model with
                                MainChat = (ChatId.Int chatId) }

                       sendMessageBase model.MainChat "This is now my main chat" None None None None None)
                      |> bot)

              cmdScan
                  "/done %s"
                  (fun taskName _ ->
                      (match user with
                       | None ->
                           "I don't recognize you. Thanks for cleaning something, though.
                           But I won't update the Putzplan unless one of the main people says so.
                           One of them should just claim it for themselves."
                           |> (sendReplyDelKeyboard >> bot)
                       | Some person ->
                           let finishedTaskName: string option =
                               parseTaskFromPutzplan model.Putzplan taskName


                           match finishedTaskName with
                           | None ->
                               "Sorry I didn't recognize that task. In the App you can just use /done"
                               |> (sendReplyDelKeyboard >> bot)
                           | Some task ->
                               do
                                   (sprintf "I think you did %s" task)
                                   |> (sendReplyDelKeyboard >> bot)

                               let newPutzplan =
                                   model.Putzplan
                                   |> List.map
                                       (fun td ->
                                           match (td.Name.ToLower(), task) with
                                           | ("müll1", "müll2") ->
                                               printfn "branch (Müll1, Müll2)"
                                               resetTime (Some DateTime.Now) td
                                           | ("müll2", "müll1") ->
                                               printfn "branch (Müll2, Müll1)"
                                               resetTime (Some DateTime.Now) td
                                           | (a, b) when a = b ->
                                               printfn "branch (a, b) for a=%s = b=%s" a b
                                               doneTask (Some DateTime.Now) person td
                                           | _ ->
                                               printfn "branch 4 with td.Name = %s and task = %s" td.Name task
                                               td)

                               updateModelWith { model with Putzplan = newPutzplan }))


              cmd
                  "/done"
                  (fun _ ->
                      (let keyboard =
                          makeKeyboard
                              "/done"
                              (model.Putzplan
                               |> List.filter (fun y -> y.CurrentPerson = user))

                       let markup =
                           Markup.ReplyKeyboardMarkup
                               { Keyboard = keyboard
                                 ResizeKeyboard = None
                                 OneTimeKeyboard = Some true
                                 Selective = Some true }

                       sendKeyboard "What did you do?" markup)
                      |> bot)


              cmd
                  "/ls"
                  (fun _ ->
                      (sendMsg (ChatId.Int chatId) (listAllTodos defaultNamingFun model.Putzplan))
                      |> bot)


              cmd
                  "/remind"
                  (fun _ ->
                      let timestamp = DateTime.Now
                      let answer = reminderString model.Putzplan
                      bot (sendMsg model.MainChat answer))

              cmdScan
                  "/bump %s"
                  (fun taskName _ ->
                      (let mutable incTask = None

                       let newPutzplan =
                           List.map
                               (fun td ->
                                   if td.Name = taskName then
                                       incTask <- Some td

                                       { td with
                                             Priority = increasePriority td.Priority }
                                   else
                                       td)
                               model.Putzplan

                       updateModelWith { model with Putzplan = newPutzplan }



                       match incTask with
                       | None ->
                           sendReplyDelKeyboard "Sorry I didn't recognize that task. In the App you can just use /bump"
                           |> bot
                       | Some t ->
                           sendReplyDelKeyboard (sprintf "I think you meant %s" t.Name)
                           |> bot

                           match t.CurrentPerson with
                           | None ->
                               sendMsg
                                   model.MainChat
                                   (sprintf
                                       "Hey everyone, it seems like somebody should do %s.\nIt's priority is now %A"
                                       t.Name
                                       (increasePriority t.Priority))
                               |> bot
                           | Some p ->
                               sendMsg
                                   model.MainChat
                                   (sprintf
                                       "Hey %s, it seems like you should do %s.\nIt's priority is now %A"
                                       (mention p)
                                       t.Name
                                       (increasePriority t.Priority))
                               |> bot))



              cmd
                  "/bump"
                  (fun _ ->
                      (let keyboard = makeKeyboard "/bump" (model.Putzplan)

                       let markup =
                           Markup.ReplyKeyboardMarkup
                               { Keyboard = keyboard
                                 ResizeKeyboard = None
                                 OneTimeKeyboard = Some true
                                 Selective = Some true }

                       sendKeyboard "What do you want to increase in priority?" markup)
                      |> bot)

              cmdScan
                  "/debump %s"
                  (fun taskName _ ->
                      (let mutable decTask = None

                       let newPutzplan =
                           List.map
                               (fun td ->
                                   if td.Name = taskName then
                                       decTask <- Some td

                                       { td with
                                             Priority = decreasePriority td.Priority }
                                   else
                                       td)
                               model.Putzplan

                       updateModelWith { model with Putzplan = newPutzplan }



                       match decTask with
                       | None ->
                           sendReplyDelKeyboard "Sorry I didn't recognize that task. In the App you can just use /bump"
                           |> bot
                       | Some t ->
                           sendReplyDelKeyboard (
                               sprintf
                                   "I think you meant %s. It's priority is now %A"
                                   t.Name
                                   (decreasePriority t.Priority)
                           )
                           |> bot))

              cmd
                  "/debump"
                  (fun _ ->
                      (let keyboard = makeKeyboard "/debump" (model.Putzplan)

                       let markup =
                           Markup.ReplyKeyboardMarkup
                               { Keyboard = keyboard
                                 ResizeKeyboard = None
                                 OneTimeKeyboard = Some true
                                 Selective = Some true }

                       sendKeyboard "What do you want to decrease in priority?" markup)
                      |> bot)

              cmd "/editInterval" ignore

              cmd
                  "/dump"
                  (fun _ ->
                      (sendMsg (ChatId.Int chatId) (sprintf "%A" model))
                      |> bot)

              cmd
                  "/rmk"
                  (fun _ ->
                      (let markup =
                          Markup.ReplyKeyboardRemove
                              { RemoveKeyboard = true
                                Selective = None }

                       bot (sendKeyboard "Keyboard was removed!" markup)))

              cmd "/version" (fun _ -> bot (sendReply "v0.2")) ]

    // Here we actually return from onUpdate
    ()

//----------------------------------------



let checkForUpdates = ignore
// I will put stuff in here that checks every hour or so if someone has to be reminded
// It will run independently in parallel to the main bot


[<EntryPoint>]
let main _ =
    // printfn "%A" x

    if File.Exists(PutzplanFileName) then
        updateModelWith (readPutzplan ())
        log "Read the old Putzplan"
    else
        log "Couldn't find old Putzplan. Going with default now."

    try
        startBot
            config
            (fun context ->
                log (sprintf "%A" context)

                match context.Update.Message with
                | None -> ()
                | Some m -> onMessage context)
            None
        |> Async.RunSynchronously
    with
    | Failure (e) -> log (sprintf "%A" e)

    0
