namespace XLWebServices.Services;

public class FallibleService<T> where T : class
{
    private readonly ILogger<FallibleService<T>> _logger;
    private readonly T? _instance;
    
    public bool HasFailed { get; private set; }

    public FallibleService(IServiceProvider serviceProvider, ILogger<FallibleService<T>> logger)
    {
        _logger = logger;
        try
        {
            _instance = ActivatorUtilities.CreateInstance<T>(serviceProvider);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Could not instantiate: {typeof(T)}");
            HasFailed = true;
        }
    }

    public async Task RunFallibleAsync(Func<T, Task> predicate)
    {
        try
        {
            await predicate(_instance!);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Fallible run has failed on {typeof(T)}");
            HasFailed = true;
            return;
        }

        HasFailed = false;
    }

    public async Task<TRet?> RunFallibleAsync<TRet>(Func<T, Task<TRet>> predicate) where TRet : struct
    {
        try
        {
            return await predicate(_instance!);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Fallible run has failed on {typeof(T)}");
            HasFailed = true;

            return default(TRet);
        }

        HasFailed = false;
    }

    public T? Get()
    {
        return _instance;
    }
}