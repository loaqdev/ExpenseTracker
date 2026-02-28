# Expense Tracker
Expense Tracker is a simple .NET console application for tracking expenses. The app stores data in expenses.json and supports commands to add, update, delete, list, clear, and summarize expenses. It can run in non-interactive (command-line) mode and interactive mode (when started without arguments). The app supports cancellation via Ctrl+C.

Inspired by: https://roadmap.sh/projects/expense-tracker

## Features
- `Add — add a new expense`
- `Update — update an expense by Id`
- `Delete — remove an expense by Id`
- `List — display all expenses`
- `Clear — remove all expenses`
- `Summary — show totals (all time or by month)`
- `Interactive mode (no args)`
- `Cancellation with Ctrl+C (CancellationToken)`

## Requirements
.NET SDK (6.0+ recommended)
Read/write permission for the application directory (to create/update expenses.json)
### Build & Run
```
dotnet build
dotnet run -- add "milk and bread" 5.50
```
### Run in interactive mode:
```dotnet run```

## Command format & examples
Description may contain multiple words if it in double quotes.

Amount accepts both . and , as decimal separators.
### Examples:
```
# add an expense (short description, dot decimal)
dotnet run -- add "milk" 2.00

# add an expense (multi-word description, comma decimal)
dotnet run -- add "buy milk and bread" 5,50

# update
dotnet run -- update 3 "buy apples and bananas" 12.30

# delete
dotnet run -- delete 3

# list
dotnet run -- list

# clear
dotnet run -- clear

# summary (all)
dotnet run -- summary

# summary for specific month (March)
dotnet run -- summary --month 3
```

##Interactive example:
```
>expense-tracker add "buy milk and eggs" 4.50
>expense-tracker list
>expense-tracker exit
```
## Contributing
Contributions welcome — open an issue or pull request on the repository.
