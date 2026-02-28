using expense_tracker.Models;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace expense_tracker.Services;

internal class FileIOService
{
    private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private const int MAXRETRIES = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(200);

    public async Task SaveAsync(FileInfo file, List<ExpenseModel> items, CancellationToken ct)
    {
        var temp = Path.Combine(file.DirectoryName!, Path.GetRandomFileName());

        try
        {
            using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(fs, items, _jsonOptions, ct).ConfigureAwait(false);
                await fs.FlushAsync(ct).ConfigureAwait(false);
            }

            File.Replace(temp, file.FullName, null);
        }
        catch (OperationCanceledException) { throw; }
        catch (DirectoryNotFoundException)
        {
            Directory.CreateDirectory(file.DirectoryName!);
            throw;
        }
        catch (UnauthorizedAccessException uax)
        {
            Console.Error.WriteLine($"Access denied to file '{file.FullName}': {uax.Message}");
            throw;
        }
        catch (IOException ioex)
        {
            Console.Error.WriteLine($"I/O error saving '{file.FullName}': {ioex.Message}");
            throw;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Unexpected error saving '{file.FullName}': {ex.GetType().Name}: {ex.Message}");
            throw;
        }
        finally
        {
            if (File.Exists(temp))
            {
                try { File.Delete(temp); } catch { }
            }
        }
    }

    public async Task<List<ExpenseModel>> LoadAsync(FileInfo file, CancellationToken cancellationToken)
    {
        if (file is null)
            throw new ArgumentNullException(nameof(file));

        cancellationToken.ThrowIfCancellationRequested();

        if (!file.Exists)
            return new List<ExpenseModel>();

        for (int attempt = 1; ; attempt++)
        {
            try
            {
                using var fs = new FileStream(
                    file.FullName,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite,
                    bufferSize: 4096,
                    useAsync: true);

                if (fs.Length == 0)
                    return new List<ExpenseModel>();

                var result = await JsonSerializer
                    .DeserializeAsync<List<ExpenseModel>>(fs, _jsonOptions, cancellationToken)
                                              .ConfigureAwait(false);

                return result ?? new List<ExpenseModel>();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (JsonException jex)
            {
                Console.Error.WriteLine($"Failed to parse data file '{file.FullName}': {jex.Message}");
                return new List<ExpenseModel>();
            }
            catch (UnauthorizedAccessException uax)
            {
                Console.Error.WriteLine($"Access denied to file '{file.FullName}': {uax.Message}");
                throw;
            }
            catch (DirectoryNotFoundException dnfx)
            {
                Console.Error.WriteLine($"Directory not found for '{file.FullName}': {dnfx.Message}");
                return new List<ExpenseModel>();
            }
            catch (IOException ioex) when (attempt < MAXRETRIES)
            {
                Console.Error.WriteLine($"I/O error reading '{file.FullName}' (attempt {attempt}/{MAXRETRIES}): {ioex.Message}");
                await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException ioex)
            {
                Console.Error.WriteLine($"I/O error reading '{file.FullName}': {ioex.Message}");
                throw;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error reading '{file.FullName}': {ex.GetType().Name}: {ex.Message}");
                throw;
            }
        }
    }
}