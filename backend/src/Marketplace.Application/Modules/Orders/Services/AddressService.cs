using Marketplace.Application.Common.Interfaces;
using Marketplace.Application.Common.Models;
using Marketplace.Application.Modules.Orders.Abstractions;
using Marketplace.Application.Modules.Orders.DTOs;
using Marketplace.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Application.Modules.Orders.Services;

/// <summary>
/// Customer delivery addresses. Every read and write is filtered by the caller's user id
/// in the query, so a guessed address id can never expose or mutate another account.
/// </summary>
public sealed class AddressService(
    IRepository<UserAddress> addresses,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    IClock clock) : IAddressService
{
    public async Task<IReadOnlyList<AddressResponse>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await addresses.Query().AsNoTracking()
            .Where(a => a.UserId == currentUser.UserId)
            .OrderByDescending(a => a.IsDefault)
            .ThenBy(a => a.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(Map).ToList();
    }

    public async Task<Result<AddressResponse>> CreateAsync(CreateAddressRequest request, CancellationToken cancellationToken = default)
    {
        var existingCount = await addresses.CountAsync(a => a.UserId == currentUser.UserId, cancellationToken).ConfigureAwait(false);

        var address = UserAddress.Create(
            currentUser.UserId,
            request.Label,
            request.RecipientName,
            request.PhoneNumber,
            request.Line1,
            request.Line2,
            request.City,
            request.State,
            request.PostalCode,
            request.Country,
            request.IsDefault || existingCount == 0,
            clock.UtcNow);

        if (address.IsDefault)
        {
            await ClearDefaultsAsync(currentUser.UserId, cancellationToken).ConfigureAwait(false);
        }

        await addresses.AddAsync(address, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<AddressResponse>.Success(Map(address));
    }

    public async Task<Result<AddressResponse>> UpdateAsync(Guid id, UpdateAddressRequest request, CancellationToken cancellationToken = default)
    {
        var address = await addresses.Query()
            .FirstOrDefaultAsync(a => a.Id == id && a.UserId == currentUser.UserId, cancellationToken)
            .ConfigureAwait(false);

        if (address is null)
        {
            return Result<AddressResponse>.Failure("Address not found.", ResultErrorCodes.NotFound);
        }

        address.Update(
            request.Label, request.RecipientName, request.PhoneNumber, request.Line1,
            request.Line2, request.City, request.State, request.PostalCode, request.Country,
            clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result<AddressResponse>.Success(Map(address));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var address = await addresses.Query()
            .FirstOrDefaultAsync(a => a.Id == id && a.UserId == currentUser.UserId, cancellationToken)
            .ConfigureAwait(false);

        if (address is null)
        {
            return Result.Failure("Address not found.", ResultErrorCodes.NotFound);
        }

        addresses.Remove(address);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result> SetDefaultAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var address = await addresses.Query()
            .FirstOrDefaultAsync(a => a.Id == id && a.UserId == currentUser.UserId, cancellationToken)
            .ConfigureAwait(false);

        if (address is null)
        {
            return Result.Failure("Address not found.", ResultErrorCodes.NotFound);
        }

        await ClearDefaultsAsync(currentUser.UserId, cancellationToken).ConfigureAwait(false);
        address.MakeDefault();

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    private async Task ClearDefaultsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var current = await addresses.Query()
            .Where(a => a.UserId == userId && a.IsDefault)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var address in current)
        {
            address.ClearDefault();
        }
    }

    private static AddressResponse Map(UserAddress a) => new(
        a.Id, a.Label, a.RecipientName, a.PhoneNumber, a.Line1, a.Line2,
        a.City, a.State, a.PostalCode, a.Country, a.IsDefault, a.CreatedAt);
}
