namespace XoHub.Server.Services;

public class RoomPruner : BackgroundService
{
    private readonly TimeSpan _roomCleanupInterval;
    private readonly TimeSpan _keyRotationInterval;
    private readonly TimeSpan _roomInactivityThreshold;
    private DateTime _lastKeyRotationCheck = DateTime.MinValue;

    private readonly IKeyManager _keyManager;
    private readonly ILogger<RoomPruner> _logger;
    private readonly IRoomManager _roomManager;
    private readonly IConfiguration _configuration;

    public RoomPruner(IRoomManager roomManager, IKeyManager keyManager, ILogger<RoomPruner> logger, IConfiguration configuration)
    {
        _roomManager = roomManager;
        _keyManager = keyManager;
        _logger = logger;
        _configuration = configuration;

        _roomCleanupInterval = TimeSpan.FromMinutes(_configuration.GetValue<double>("RoomPruner:CleanupIntervalMinutes", 5));
        _keyRotationInterval = TimeSpan.FromHours(_configuration.GetValue<double>("RoomPruner:KeyRotationHours", 1));
        _roomInactivityThreshold = TimeSpan.FromMinutes(_configuration.GetValue<double>("RoomPruner:InactivityThresholdMinutes", 30));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RoomPruner started. Room cleanup: every {CleanupInterval}, Key rotation: every {RotationInterval}", _roomCleanupInterval, _keyRotationInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Wait for cleanup interval
                await Task.Delay(_roomCleanupInterval, stoppingToken);

                // Remove rooms inactive > threshold
                var inactiveRooms = _roomManager.GetInactiveRooms(_roomInactivityThreshold);
                foreach (var roomId in inactiveRooms)
                {
                    try
                    {
                        _roomManager.RemoveRoom(roomId);
                        _logger.LogInformation("Pruned inactive room: {RoomId}", roomId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to remove inactive room {RoomId}", roomId);
                    }
                }

                // Rotate JWT keys when interval elapsed
                if (DateTime.UtcNow - _lastKeyRotationCheck >= _keyRotationInterval)
                {
                    try
                    {
                        _keyManager.RotateKeys();
                        _lastKeyRotationCheck = DateTime.UtcNow;
                        _logger.LogDebug("Checked key rotation (keys rotated if needed)");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Key rotation failed");
                        // Continue running - don't let key rotation failures stop the service
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Cancellation requested, exit the loop
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in RoomPruner execution loop");
                // Continue the loop - don't let one error stop the entire service
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); // Brief pause before retry
            }
        }

        _logger.LogInformation("RoomPruner stopped");
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("RoomPruner stopping...");
        await base.StopAsync(cancellationToken);
    }
}