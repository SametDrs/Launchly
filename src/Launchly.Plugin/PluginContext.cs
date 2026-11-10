namespace Launchly.Plugin;

/// <summary>Eklentiye <see cref="IPlugin.InitAsync"/> sırasında verilen ortam bilgisi.</summary>
/// <param name="DataDirectory">Eklentinin kendi verisini yazabileceği klasör (var olması garanti edilir).</param>
public sealed record PluginContext(string DataDirectory);
