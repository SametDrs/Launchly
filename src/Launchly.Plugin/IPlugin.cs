namespace Launchly.Plugin;

/// <summary>Launchly eklentisi. Tek async arayüz: önce <see cref="InitAsync"/>, sonra her sorguda <see cref="QueryAsync"/>.</summary>
public interface IPlugin
{
    PluginMetadata Metadata { get; }

    /// <summary>Uygulama açılışında bir kez çağrılır. Fırlatırsa eklenti devre dışı kalır.</summary>
    Task InitAsync(PluginContext context);

    /// <summary>
    /// Sorguya sonuç üretir. <paramref name="cancellationToken"/> yeni sorgu yazıldığında iptal edilir;
    /// eklenti mümkün olan en kısa sürede dönmelidir.
    /// </summary>
    Task<IReadOnlyList<Result>> QueryAsync(Query query, CancellationToken cancellationToken);
}
