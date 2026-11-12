using Launchly.Plugin;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launchly.Plugins.Calculator;

/// <summary>Panoya metin koyar; testlerde sahtesi kullanılır.</summary>
public interface IClipboardService
{
    void SetText(string text);
}

/// <summary>WPF panosu (UI iş parçacığında çağrılmalı).</summary>
public sealed class WpfClipboardService : IClipboardService
{
    public void SetText(string text) =>
        System.Windows.Clipboard.SetDataObject(text, copy: true);
}

/// <summary>"2+2*3" gibi ifadeleri hesaplar; Enter sonucu panoya kopyalar.</summary>
public sealed class CalculatorPlugin : IPlugin
{
    public const string PluginId = "launchly.calculator";

    private readonly IClipboardService _clipboard;
    private readonly ILogger _logger;

    public CalculatorPlugin(IClipboardService clipboard, ILogger<CalculatorPlugin> logger)
        : this(clipboard, (ILogger)logger)
    {
    }

    internal CalculatorPlugin(IClipboardService clipboard, ILogger? logger)
    {
        _clipboard = clipboard;
        _logger = logger ?? NullLogger.Instance;
    }

    public PluginMetadata Metadata { get; } = new() { Id = PluginId, Name = "Calculator", Description = "Yazdığın matematik ifadesini hesaplar; sonucu Enter ile kopyalar." };

    public Task InitAsync(PluginContext context) => Task.CompletedTask;

    public Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken cancellationToken)
    {
        var text = query.Raw.Trim();
        if (!ExpressionEvaluator.LooksLikeMath(text) || !ExpressionEvaluator.TryEvaluate(text, out var value))
        {
            return Task.FromResult<IReadOnlyList<Result>>([]);
        }

        var formatted = ExpressionEvaluator.Format(value);
        var result = new Result
        {
            Title = formatted,
            SubTitle = $"{text} = {formatted}  (Enter: panoya kopyala)",
            Icon = "", // Calculator
            Score = 900,
            TypeLabel = "Calculator",
            AutoComplete = formatted,
            Action = () => Task.FromResult(Copy(formatted)),
        };
        return Task.FromResult<IReadOnlyList<Result>>([result]);
    }

    private bool Copy(string text)
    {
        try
        {
            _clipboard.SetText(text);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pano yazılamadı");
            return false;
        }
    }
}
