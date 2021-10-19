module Types

open System
open System.Text.RegularExpressions
open Funogram.Telegram.Types

type Flatmate = { Name: string; Id: int64 }

let flatmateFromUserHelper (allFlatmates: Flatmate list) (user: User) =
    List.tryFind (fun fm -> fm.Id = user.Id) allFlatmates

let mention (flatmate: Flatmate) =
    "["
    + (flatmate.Name)
    + (sprintf "](tg://user?id=%i)" flatmate.Id)

// type FlatmateApi =
//     { FlatmateName: Secret.Flatmate -> string
//       FlatmateFromUser: User -> Secret.Flatmate option
//       FlatmateToId: Secret.Flatmate -> int64
//       Token: string
//       DefaultOrder: Secret.Flatmate list
//       DefaultMainChat: int64 }

// let flatmateApi =
//     { FlatmateName = Secret.flatmateName
//       FlatmateFromUser = Secret.flatmateFromUser
//       FlatmateToId = Secret.flatmateToId
//       Token = Secret.Token
//       DefaultOrder = Secret.defaultOrder
//       DefaultMainChat = Secret.defaultMainChat }

type Priority =
    | Low
    | Middle
    | High

let increasePriority =
    function
    | Low -> Middle
    | _ -> High

let decreasePriority =
    function
    | High -> Middle
    | _ -> Low

// All Todo Helpers

type Todo =
    { CurrentPerson: Flatmate option
      LastDone: DateTime option
      LastReminded: DateTime option
      Interval: int // How many days should be between doing the task at most
      Priority: Priority
      Name: string
      Desc: string option
      Order: Flatmate list }

type Putzplan = Todo list

let resetTime time todo =
    { todo with
          LastDone = time
          LastReminded = None
          Priority = Low }

let doneTask time person todo =
    // When a person does a task its LastDone and Priority get resetted
    // If it is the person who war responsible for it,
    // Then it goes to the next person in order
    let nextInLine (xs: 'a list) (x: 'a) : 'a option =
        match List.tryFindIndex (fun y -> y = x) xs with
        | None -> None
        | Some n ->
            if n + 1 = List.length xs then
                Some(List.head xs)
            else
                List.tryItem (n + 1) xs

    let nextResponsible =
        if Some person = todo.CurrentPerson then
            nextInLine todo.Order person
        else
            todo.CurrentPerson

    { todo with
          CurrentPerson = nextResponsible
          LastDone = time
          LastReminded = None
          Priority = Low }

let incPrio todo =
    { todo with
          Priority = increasePriority todo.Priority }

let decPrio todo =
    { todo with
          Priority = decreasePriority todo.Priority }


let isOverdue (time: DateTime) todo =
    match todo.LastDone with
    | None -> false
    | Some t -> time.Subtract(t).Days > todo.Interval

let needsToBeReminded (time: DateTime) todo =
    match todo.LastReminded with
    | None -> isOverdue time todo
    | Some t ->
        (time.Subtract(t).Days > 1)
        && (isOverdue time todo)


let listUserTodos (namingFun: Flatmate option -> string) (putzplan: list<Todo>) user =
    let name = namingFun user

    let prioString todo =
        let forReminder =
            if needsToBeReminded DateTime.Now todo then
                "❗️"
            else
                ""

        let forPrio =
            match todo.Priority with
            | Low -> ""
            | Middle -> "😡"
            | High -> "🤬"

        forReminder + forPrio


    let rec buildListString result tasks =
        match tasks with
        | [] -> result
        | x :: xs ->

            let step =
                String.concat
                    ""
                    [ result
                      "    "
                      prioString x
                      x.Name
                      "\n" ]
            // String.concat result [ "    "; prioString x; x.Name; "\n" ]

            buildListString step xs

    buildListString ("**" + name + "**\n") (List.filter (fun y -> y.CurrentPerson = user) putzplan)

let listAllTodos namingFun putzplan =
    putzplan
    |> List.map (fun x -> x.CurrentPerson)
    |> List.distinct
    |> List.map (listUserTodos namingFun putzplan)
    |> String.concat ""

// [inline mention of a user](tg://user?id=123456789)
let reminderString putzplan =
    putzplan
    |> List.filter (needsToBeReminded DateTime.Now)
    |> listAllTodos (
        Option.map (mention)
        >> Option.defaultValue "Unassigned"
    )

let changeInterval todo newInterval = { todo with Interval = newInterval }

let parseTaskFromPutzplan putzplan (taskname: string) =
    let allTaskNames =
        List.map (fun x -> x.Name.ToLower()) putzplan

    let contains longstring substring =
        Regex.Match(substring, longstring).Success

    // This does not really work because of "kitchen" and "mop the kitchen"
    // List.tryFind (contains (taskname.ToLower())) allTaskNames
    List.tryFind (fun x -> x = taskname.ToLower()) allTaskNames
