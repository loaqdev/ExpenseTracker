using expense_tracker.Models;
using expense_tracker.Services;
using System.CommandLine;
using System.Text.Json;

internal static class Program
{
    private static readonly FileIOService _fileIOService = new();
    private static readonly string _filePath = Path.Combine(AppContext.BaseDirectory, "expenses.json");
    private static readonly FileInfo _fileInfo = new(_filePath);

    public static async Task<int> Main(string[] args)
    {
        using var cts = new CancellationTokenSource();

        Console.CancelKeyPress += (s, e) =>
        {
            Console.WriteLine("Cancelling...");
            e.Cancel = true;
            cts.Cancel();
        };

        var root = new RootCommand("Expense Tracker");

        var idArg = new Argument<int>("id") { Arity = ArgumentArity.ExactlyOne };
        var descArg = new Argument<string>("description") { Arity = ArgumentArity.ExactlyOne };
        var monthOption = new Option<int>("--month") { DefaultValueFactory = (_) => 0 };
        var amountArg = new Argument<decimal>("amount")
        {
            CustomParser = (result) =>
            {
                var token = cts.Token;
                token.ThrowIfCancellationRequested();
                var input = result.Tokens.SingleOrDefault()?.Value;
                return ConvertNumberToCurrentLocale(input);
            }
        };

        // == Commands ==

        // add
        var add = new Command("add", "Add expense" +
            "\nExample: add Coffee 3,5" +
            "\nPut the description in quotation marks (\"Description\") to write a sentence.")
        { descArg, amountArg };
        add.SetAction(async (parseResult, cts) =>
        {
            var description = parseResult.GetValue(descArg);
            var amount = parseResult.GetValue(amountArg);
            if (string.IsNullOrWhiteSpace(description) || amount <= 0)
            {
                Console.WriteLine("Invalid input. Usage: add \"Description\" Amount (>0)");
                return;
            }
            await AddExpenseAsync(_fileInfo, description.Trim(), amount, cts).ConfigureAwait(false);
        });
        root.Add(add);

        // update
        var update = new Command("update", "Update expense" +
            "\nExample: update 2 Water 2,8" +
            "\nPut the new description in quotation marks (\"NewDescription\") to write a sentence.\"")
        { idArg, descArg, amountArg };
        update.SetAction(async (parseResult, cts) =>
        {
            var id = parseResult.GetValue(idArg);
            var newDesc = parseResult.GetValue(descArg);
            var newAmount = parseResult.GetValue(amountArg);
            if (id <= 0 || string.IsNullOrWhiteSpace(newDesc) || newAmount <= 0)
            {
                Console.WriteLine("Invalid input. Usage: update Id \"New description\" NewAmount (>0)");
                return;
            }

            await UpdateExpenseAsync(_fileInfo, id, newDesc.Trim(), newAmount, cts).ConfigureAwait(false);
        });
        root.Add(update);

        // delete
        var delete = new Command("delete", "Delete expense.\nExample: delete 4") { idArg };
        delete.SetAction(async (parseResult, cts) =>
        {
            var id = parseResult.GetValue(idArg);
            if (id <= 0)
            {
                Console.WriteLine("Invalid input. Usage: delete Id");
                return;
            }

            await DeleteExpenseAsync(_fileInfo, id, cts).ConfigureAwait(false);
        });
        root.Add(delete);

        // list
        var list = new Command("list", "List expenses");
        list.SetAction(async (parseResult, cts) => await ListExpensesAsync(_fileInfo, cts).ConfigureAwait(false));
        root.Add(list);

        // clear
        var clear = new Command("clear", "Delete all expenses from file");
        clear.SetAction(async (parseResult, cts) => await ClearExpensesAsync(_fileInfo, cts).ConfigureAwait(false));
        root.Add(clear);

        // exit
        var exit = new Command("exit", "To quit the app");
        root.Add(exit);

        // summary
        var summary = new Command("summary", "Summary of expenses." +
            "\nExample: summary" +
            "\nor: summary --month 2")
        { monthOption };
        summary.SetAction(async (parseResult, cts) =>
        {
            var month = parseResult.GetValue(monthOption);
            if (month != 0 && (month < 1 || month > 12))
            {
                Console.WriteLine("Month must be between 1 and 12 (or 0 for all).");
                return;
            }

            if (month > 0)
                await SummaryOfSpecificMonthAsync(_fileInfo, month, cts).ConfigureAwait(false);
            else
                await SummaryAllAsync(_fileInfo, cts).ConfigureAwait(false);
        });
        root.Add(summary);

        if (args.Length == 0)
        {
            await RunInteractiveLoopAsync(root, cts.Token).ConfigureAwait(false);
            return 0;
        }

        try
        {
            return await root.Parse(args).InvokeAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Operation cancelled by user.");
            return 1;
        }
        catch (AggregateException ae) when (ae.InnerExceptions.All(e => e is OperationCanceledException))
        {
            Console.WriteLine("All operations were cancelled.");
            return 1;
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.Error.WriteLine($"Access denied: {ex.Message}");
            return 3;
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine($"Data format error: {ex.Message}");
            return 4;
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"File I/O error: {ex.Message}");
            return 5;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Unexpected error: {ex.GetType().Name}: {ex.Message}");
            return 2;
        }
    }

    private static async Task RunInteractiveLoopAsync(RootCommand root, CancellationToken token)
    {
        Console.WriteLine("Welcome to Expense tracker." +
            "\n Type '--help' to see list of commands");
        while (!token.IsCancellationRequested)
        {
            Console.Write(">expense-tracker ");
            var line = Console.ReadLine();
            if (line == null) break;
            line = line.Trim();
            if (string.Equals(line, "exit", StringComparison.OrdinalIgnoreCase)) break;
            if (line.Length == 0) continue;

            try
            {
                await root.Parse(line).InvokeAsync()
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
            }
        }

        Console.WriteLine("Exiting interactive mode...");
    }

    private static async Task AddExpenseAsync(FileInfo file, string description, decimal amount, CancellationToken token)
    {
        Console.WriteLine("Adding new expense...");
        var items = await _fileIOService.LoadAsync(file, token)
            .ConfigureAwait(false) ?? new List<ExpenseModel>();
        var nextId = items.Count == 0 ? 1 : items.Max(i => i.Id) + 1;
        var item = new ExpenseModel
        {
            Id = nextId,
            Description = Truncate(description, 200),
            Amount = amount,
            Date = DateTime.UtcNow.Date
        };
        items.Add(item);
        await _fileIOService.SaveAsync(file, items, token)
            .ConfigureAwait(false);
        Console.WriteLine($"Added expense '{item.Description}' with id {item.Id}");
    }

    private static async Task UpdateExpenseAsync(FileInfo file, int id, string newDescription, decimal newAmount, CancellationToken token)
    {
        var items = await _fileIOService.LoadAsync(file, token)
            .ConfigureAwait(false) ?? new List<ExpenseModel>();
        var item = items.SingleOrDefault(i => i.Id == id);
        if (item == null)
        {
            Console.WriteLine($"Item with id {id} not found.");
            return;
        }

        item.Description = Truncate(newDescription, 200);
        item.Amount = newAmount;
        await _fileIOService.SaveAsync(file, items, token)
            .ConfigureAwait(false);
        Console.WriteLine($"Updated expense with id {id} with new description {newDescription}");
    }

    private static async Task DeleteExpenseAsync(FileInfo file, int id, CancellationToken token)
    {
        var items = await _fileIOService.LoadAsync(file, token)
            .ConfigureAwait(false) ?? new List<ExpenseModel>();
        var removed = items.RemoveAll(i => i.Id == id);
        if (removed == 0)
        {
            Console.WriteLine($"No expense with id {id} to delete.");
            return;
        }

        await _fileIOService.SaveAsync(file, items, token)
            .ConfigureAwait(false);
        Console.WriteLine($"Deleted expense {id}");
    }

    private static async Task ListExpensesAsync(FileInfo file, CancellationToken token)
    {
        var items = await _fileIOService.LoadAsync(file, token)
            .ConfigureAwait(false) ?? new List<ExpenseModel>();
        Console.WriteLine("{0,-4} {1,-12} {2,-40} {3,10}", "ID", "Date", "Description", "Amount");

        foreach (var i in items.OrderBy(x => x.Id))
        {
            Console.WriteLine("{0,-4} {1,-12:yyyy-MM-dd} {2,-40} {3,10} $",
                i.Id, i.Date, Truncate(i.Description, 40), i.Amount);
        }
    }

    private static async Task ClearExpensesAsync(FileInfo file, CancellationToken token)
    {
        Console.WriteLine("Clearing all expenses...");
        var empty = new List<ExpenseModel>();
        await _fileIOService.SaveAsync(file, empty, token)
            .ConfigureAwait(false);
        Console.WriteLine("All expenses cleared.");
    }

    private static async Task SummaryAllAsync(FileInfo file, CancellationToken token)
    {
        var items = await _fileIOService.LoadAsync(file, token)
            .ConfigureAwait(false) ?? new List<ExpenseModel>();
        var total = items.Sum(i => i.Amount);
        Console.WriteLine($"Total expenses: {total} $");
    }

    private static async Task SummaryOfSpecificMonthAsync(FileInfo file, int month, CancellationToken token)
    {
        var items = await _fileIOService.LoadAsync(file, token)
            .ConfigureAwait(false) ?? new List<ExpenseModel>();
        var total = items.Where(i => i.Date.Month == month).Sum(i => i.Amount);
        string monthName = new DateTime(1, month, 1).ToString("MMMM");
        Console.WriteLine($"Total expenses for {monthName}: {total} $");
    }

    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return s ?? string.Empty;
        return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
    }

    private static decimal ConvertNumberToCurrentLocale(string? input)
    {
        string separator = Thread.CurrentThread.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        switch (separator)
        {
            case ".":
                input = input?.Replace(",", ".");
                break;
            case ",":
                input = input?.Replace(".", ",");
                break;
        }
        decimal.TryParse(input, out var number);
        return number;
    }
}
