# Putzplanbot

I built this Telegram bot to keep track of the various tasks in a shared flat. 
The basic structure of the way tasks are shared is the following:

    * Every task goes around (maybe a subset) the people.
    * People are supposed to do the tasks whenever is most convenient for them.
        * Tasks do have a suggested intervall of days, indicating how often it should be done. 
    * If someone feels that a task should be done, you can increase the priority.
    * The bot is built under the assumption that there is a group chat with everybody and the bot, but people can also interact with the bot independently.

    
I feel strongly that this is better than the, what I experienced as the most common model, variant where every task is supposed to be done e.g. once per week and every week the tasks just wander around all at the same time.

# How to use

You have to add a `Secret.fs` file, which includes the information I did not want to publicise on github. You have to add your Telegram bot token, a list of all flatmates, an initial cleaning plan, and a default group chat. See the `SecretApi` type in the `Program.fs` file. 

To define the members of your flatshare you will need to find their Telegram ids. They are actually sent with every message and you can look at them by just having the bot dump it into a textfile. Alternatively, and more conveniently, you can use one of the many Telegram Id bots, that tell you exactly that. As far as I know, there is no way to just see it in your settings.

For the initial plan I suggest making a default todo like 
```
let defaultOrder = [Tick; Trick; Track]
let defaultTodo: Todo =
    { CurrentPerson = None
      LastDone = None
      LastReminded = None
      Interval = 0
      Priority = Low
      Name = ""
      Desc = None
      Order = defaultOrder }
```
and then creating a list.



## Api

Here is a list of the commands.

* `/ls` - Show the Putzplan
* `/done` - Gives you a keyboard of all your tasks, click the one you did
* `/done task` - Marks the task as done. If it wasn't yours it will stay with the current person
* `/bump` - Gives you a keyboard of all tasks, click the one you want to increase the priority of
* `/bump task` - Increases the priority of the task and pings the responsible person in the main chat
* `/rmk` - Should you be stuck with a custom keyboard, this will get rid of it. Should not be needed.
* `/showmewhatyouvegot` - Gives you a json of the current internal model.
* `/remind` - reminds everybody of all their **overdue!** tasks