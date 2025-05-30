using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;

namespace ActualLab.Fusion.Authentication.Services;

public interface IDbSessionInfoRepo<in TDbContext, TDbSessionInfo, in TDbUserId>
    where TDbContext : DbContext
    where TDbSessionInfo : DbSessionInfo<TDbUserId>, new()
    where TDbUserId : notnull
{
    public Type SessionInfoEntityType { get; }

    // Write methods
    public Task<TDbSessionInfo> GetOrCreate(
        TDbContext dbContext, string sessionId, CancellationToken cancellationToken = default);
    public Task<TDbSessionInfo> Upsert(
        TDbContext dbContext, string sessionId, SessionInfo sessionInfo, CancellationToken cancellationToken = default);
    public Task<int> Trim(
        string shard, DateTime maxLastSeenAt, int maxCount, CancellationToken cancellationToken = default);

    // Read methods
    public Task<TDbSessionInfo?> Get(
        string shard, string sessionId, CancellationToken cancellationToken = default);
    public Task<TDbSessionInfo?> Get(
        TDbContext dbContext, string sessionId, bool forUpdate, CancellationToken cancellationToken = default);
    public Task<TDbSessionInfo[]> ListByUser(
        TDbContext dbContext, TDbUserId userId, CancellationToken cancellationToken = default);
}

public class DbSessionInfoRepo<TDbContext,
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TDbSessionInfo,
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TDbUserId>(
    DbAuthService<TDbContext>.Options settings,
    IServiceProvider services
    ) : DbServiceBase<TDbContext>(services), IDbSessionInfoRepo<TDbContext, TDbSessionInfo, TDbUserId>
    where TDbContext : DbContext
    where TDbSessionInfo : DbSessionInfo<TDbUserId>, new()
    where TDbUserId : notnull
{
    protected DbAuthService<TDbContext>.Options Settings { get; } = settings;
    protected IDbUserIdHandler<TDbUserId> UserIdHandler { get; init; }
        = services.GetRequiredService<IDbUserIdHandler<TDbUserId>>();
    protected IDbEntityResolver<string, TDbSessionInfo> SessionResolver { get; init; }
        = services.DbEntityResolver<string, TDbSessionInfo>();
    protected IDbEntityConverter<TDbSessionInfo, SessionInfo> SessionConverter { get; init; }
        = services.DbEntityConverter<TDbSessionInfo, SessionInfo>();
    protected IDbShardResolver<TDbContext> ShardResolver { get; init; }
        = services.DbShardResolver<TDbContext>();

    public Type SessionInfoEntityType => typeof(TDbSessionInfo);

    // Write methods

    public virtual async Task<TDbSessionInfo> GetOrCreate(
        TDbContext dbContext, string sessionId, CancellationToken cancellationToken = default)
    {
        var dbSessionInfo = await Get(dbContext, sessionId, true, cancellationToken).ConfigureAwait(false);
        if (dbSessionInfo == null) {
            var session = new Session(sessionId);
            var sessionInfo = new SessionInfo(session, Clocks.SystemClock.Now);
            dbSessionInfo = dbContext.Add(
                new TDbSessionInfo() {
                    Id = sessionId,
                    CreatedAt = sessionInfo.CreatedAt,
                }).Entity;
            SessionConverter.UpdateEntity(sessionInfo, dbSessionInfo);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        return dbSessionInfo;
    }

    public async Task<TDbSessionInfo> Upsert(
        TDbContext dbContext, string sessionId, SessionInfo sessionInfo, CancellationToken cancellationToken = default)
    {
        var dbSessionInfo = await dbContext.Set<TDbSessionInfo>().ForNoKeyUpdate()
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken)
            .ConfigureAwait(false);
        var isDbSessionInfoFound = dbSessionInfo != null;
        dbSessionInfo ??= new() {
            Id = sessionId,
            CreatedAt = sessionInfo.CreatedAt,
        };
        SessionConverter.UpdateEntity(sessionInfo, dbSessionInfo);
        if (isDbSessionInfoFound)
            dbContext.Update(dbSessionInfo);
        else
            dbContext.Add(dbSessionInfo);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return dbSessionInfo;
    }

    public virtual async Task<int> Trim(
        string shard, DateTime maxLastSeenAt, int maxCount, CancellationToken cancellationToken = default)
    {
        var dbContext = await DbHub.CreateDbContext(shard, true, cancellationToken).ConfigureAwait(false);
        await using var _1 = dbContext.ConfigureAwait(false);
        dbContext.EnableChangeTracking(false);

#if NET7_0_OR_GREATER
        return await dbContext.Set<TDbSessionInfo>()
            .Where(o => o.LastSeenAt < maxLastSeenAt)
            .OrderBy(o => o.LastSeenAt)
            .Take(maxCount)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
#else
        var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var _2 = tx.ConfigureAwait(false);

        var entities = await dbContext.Set<TDbSessionInfo>(DbHintSet.UpdateSkipLocked)
            .Where(o => o.LastSeenAt < maxLastSeenAt)
            .OrderBy(o => o.LastSeenAt)
            .Take(maxCount)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (entities.Count == 0)
            return 0;

        dbContext.Set<TDbSessionInfo>().RemoveRange(entities);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        return entities.Count;
#endif
    }

    // Read methods

    public async Task<TDbSessionInfo?> Get(string shard, string sessionId, CancellationToken cancellationToken = default)
        => await SessionResolver.Get(shard, sessionId, cancellationToken).ConfigureAwait(false);

    public virtual async Task<TDbSessionInfo?> Get(
        TDbContext dbContext, string sessionId, bool forUpdate, CancellationToken cancellationToken = default)
    {
        var dbSessionInfos = forUpdate
            ? dbContext.Set<TDbSessionInfo>().ForNoKeyUpdate()
            : dbContext.Set<TDbSessionInfo>();
        return await dbSessionInfos
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken)
            .ConfigureAwait(false);
    }

    public virtual async Task<TDbSessionInfo[]> ListByUser(
        TDbContext dbContext, TDbUserId userId, CancellationToken cancellationToken = default)
    {
        var qSessions =
            from s in dbContext.Set<TDbSessionInfo>().AsQueryable()
            where Equals(s.UserId, userId)
            orderby s.LastSeenAt descending
            select s;
        var sessions = (TDbSessionInfo[]) await qSessions.ToArrayAsync(cancellationToken).ConfigureAwait(false);
        return sessions;
    }
}
