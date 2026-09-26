using FluentAssertions;
using Marketplace.Domain.Catalog;
using Marketplace.Domain.Common;
using Marketplace.Domain.Enums;
using Marketplace.Domain.Inventory;
using Xunit;
using InventoryRecord = Marketplace.Domain.Inventory.Inventory;

namespace Marketplace.UnitTests.Inventory;

public sealed class InventoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private static InventoryRecord Create(int available = 10, int threshold = 3) =>
        InventoryRecord.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), available, threshold, Now);

    [Fact]
    public void Sellable_quantity_is_available_minus_reserved()
    {
        var inventory = Create(10);
        inventory.Reserve(4, Now);

        inventory.SellableQuantity.Should().Be(6);
    }

    [Fact]
    public void Reserving_within_the_available_quantity_succeeds()
    {
        var inventory = Create(10);
        inventory.Reserve(3, Now);

        inventory.ReservedQuantity.Should().Be(3);
        inventory.AvailableQuantity.Should().Be(10);
    }

    [Fact]
    public void Reserving_more_than_is_sellable_throws()
    {
        var inventory = Create(2);
        inventory.Reserve(2, Now);

        var act = () => inventory.Reserve(1, Now);

        act.Should().Throw<InsufficientStockException>();
    }

    [Fact]
    public void Reserving_zero_or_a_negative_quantity_is_refused()
    {
        var inventory = Create(5);

        var act = () => inventory.Reserve(0, Now);
        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void Releasing_a_reservation_returns_stock_to_the_sellable_pool()
    {
        var inventory = Create(5);
        inventory.Reserve(3, Now);
        inventory.ReleaseReservation(3, Now);

        inventory.SellableQuantity.Should().Be(5);
        inventory.ReservedQuantity.Should().Be(0);
    }

    [Fact]
    public void Releasing_more_than_is_reserved_is_refused()
    {
        var inventory = Create(5);
        inventory.Reserve(2, Now);

        var act = () => inventory.ReleaseReservation(3, Now);

        act.Should().Throw<BusinessRuleException>();
    }

    [Fact]
    public void Committing_a_reservation_moves_stock_from_reserved_to_sold()
    {
        var inventory = Create(5);
        inventory.Reserve(2, Now);
        inventory.CommitSale(2, Now);

        inventory.ReservedQuantity.Should().Be(0);
        inventory.SoldQuantity.Should().Be(2);
        inventory.AvailableQuantity.Should().Be(3);
    }

    [Fact]
    public void Committing_more_than_is_reserved_is_refused()
    {
        var inventory = Create(5);
        inventory.Reserve(1, Now);

        var act = () => inventory.CommitSale(2, Now);

        act.Should().Throw<BusinessRuleException>();
    }

    [Fact]
    public void Reversing_a_sale_returns_the_units_to_stock()
    {
        var inventory = Create(5);
        inventory.Reserve(2, Now);
        inventory.CommitSale(2, Now);
        inventory.ReverseSale(2, Now);

        inventory.SoldQuantity.Should().Be(0);
        inventory.AvailableQuantity.Should().Be(5);
    }

    [Fact]
    public void A_restock_increases_the_available_quantity()
    {
        var inventory = Create(5);
        inventory.Restock(10, Now);

        inventory.AvailableQuantity.Should().Be(15);
    }

    [Fact]
    public void A_negative_adjustment_cannot_exceed_the_stock_on_hand()
    {
        var inventory = Create(5);

        var act = () => inventory.Adjust(-6, Now);
        act.Should().Throw<BusinessRuleException>();
    }

    [Fact]
    public void A_zero_adjustment_is_refused()
    {
        var inventory = Create(5);

        var act = () => inventory.Adjust(0, Now);
        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void Low_stock_is_derived_from_the_sellable_quantity_and_threshold()
    {
        var inventory = Create(5, threshold: 3);
        inventory.IsLowStock.Should().BeFalse();

        inventory.Reserve(3, Now);
        inventory.IsLowStock.Should().BeTrue();
    }

    [Fact]
    public void Out_of_stock_ignores_reserved_units()
    {
        var inventory = Create(2);
        inventory.Reserve(2, Now);

        inventory.IsOutOfStock.Should().BeTrue();
    }

    [Fact]
    public void A_customer_return_puts_the_goods_back_on_the_shelf()
    {
        var inventory = Create(3);
        inventory.ReceiveReturn(2, Now);

        inventory.AvailableQuantity.Should().Be(5);
    }

    [Fact]
    public void A_reservation_releases_only_once_even_if_repeated()
    {
        var reservation = InventoryReservation.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2, Now.AddMinutes(15), Now);
        var inventory = Create(5);
        inventory.Reserve(2, Now);

        var first = InventoryTransaction.Record(inventory, InventoryTransactionType.ReservationRelease, 2, "Order", reservation.OrderId, "release", null, Now);
        inventory.ReleaseReservation(2, Now);
        first.QuantityBefore.Should().Be(5);

        var act = () => inventory.ReleaseReservation(2, Now);
        act.Should().Throw<BusinessRuleException>();
    }

    [Fact]
    public void A_reservation_is_expired_once_its_window_passes()
    {
        var reservation = InventoryReservation.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, Now.AddMinutes(15), Now);

        reservation.IsExpired(Now.AddMinutes(14)).Should().BeFalse();
        reservation.IsExpired(Now.AddMinutes(16)).Should().BeTrue();
    }

    [Fact]
    public void Releasing_a_reservation_twice_is_a_no_op()
    {
        var reservation = InventoryReservation.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, Now.AddMinutes(15), Now);

        reservation.Release(Now, "expired");
        var act = () => reservation.Release(Now.AddMinutes(1), "expired-again");

        act.Should().NotThrow();
        reservation.IsReleased.Should().BeTrue();
    }
}

public sealed class ProductApprovalTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid SellerId = Guid.NewGuid();
    private static readonly Guid CategoryId = Guid.NewGuid();

    private static Product CreateProduct(bool withImages = true, bool withVariants = true)
    {
        var product = Domain.Catalog.Product.Create(SellerId, CategoryId, "Test Product", Slug.Create("test-product"), "Short", "Long", 100m, 150m, null, null, Now);

        if (withImages)
        {
            product.AddImage("https://cdn.example.com/1.jpg", "Test", true, Now);
        }

        if (withVariants)
        {
            product.AddVariant("SKU-1", "Default", null, 0, Now);
        }

        return product;
    }

    [Fact]
    public void A_new_product_starts_as_a_draft()
    {
        CreateProduct().Status.Should().Be(ProductStatus.Draft);
    }

    [Fact]
    public void Submitting_for_approval_without_images_is_refused()
    {
        var product = CreateProduct(withImages: false);

        var act = () => product.SubmitForApproval(Now);
        act.Should().Throw<BusinessRuleException>();
    }

    [Fact]
    public void Submitting_for_approval_without_variants_is_refused()
    {
        var product = CreateProduct(withVariants: false);

        var act = () => product.SubmitForApproval(Now);
        act.Should().Throw<BusinessRuleException>();
    }

    [Fact]
    public void Approving_moves_the_product_to_published()
    {
        var product = CreateProduct();
        product.SubmitForApproval(Now);
        product.Approve(Now);

        product.Status.Should().Be(ProductStatus.Published);
        product.IsPubliclyVisible.Should().BeTrue();
    }

    [Fact]
    public void Rejecting_records_the_reason()
    {
        var product = CreateProduct();
        product.SubmitForApproval(Now);
        product.Reject(ProductRejectionReason.PricingIssue, "Price looks wrong", Now);

        product.Status.Should().Be(ProductStatus.Rejected);
        product.RejectionNote.Should().Be("Price looks wrong");
        product.IsPubliclyVisible.Should().BeFalse();
    }

    [Fact]
    public void Editing_a_published_product_sends_it_back_through_moderation()
    {
        var product = CreateProduct();
        product.SubmitForApproval(Now);
        product.Approve(Now);

        product.UpdateDetails("Renamed", "Short", "Long", CategoryId, null, null, Now);

        product.Status.Should().Be(ProductStatus.PendingApproval);
    }

    [Fact]
    public void Only_a_published_product_can_be_featured()
    {
        var draft = CreateProduct();
        var act = () => draft.SetFeatured(true, Now);
        act.Should().Throw<BusinessRuleException>();

        draft.SubmitForApproval(Now);
        draft.Approve(Now);
        draft.SetFeatured(true, Now);

        draft.IsFeatured.Should().BeTrue();
    }

    [Fact]
    public void A_compare_at_price_below_the_selling_price_is_refused()
    {
        var act = () => Domain.Catalog.Product.Create(SellerId, CategoryId, "P", Slug.Create("p"), "s", "l", 100m, 50m, null, null, Now);

        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void The_discount_percentage_is_derived_from_the_two_prices()
    {
        var product = CreateProduct();
        product.DiscountPercentage.Should().Be(33);
    }

    [Fact]
    public void Adding_a_second_primary_image_clears_the_first()
    {
        var product = CreateProduct();
        var second = product.AddImage("https://cdn.example.com/2.jpg", "Second", true, Now);

        product.Images.Count(i => i.IsPrimary).Should().Be(1);
        second.IsPrimary.Should().BeTrue();
    }

    [Fact]
    public void Removing_the_primary_image_promotes_another_one()
    {
        var product = CreateProduct();
        product.AddImage("https://cdn.example.com/2.jpg", "Second", false, Now);
        var primary = product.Images.First(i => i.IsPrimary);

        product.RemoveImage(primary.Id, Now);

        product.Images.Should().NotBeEmpty();
        product.Images.Should().ContainSingle(i => i.IsPrimary);
    }

    [Fact]
    public void A_duplicate_sku_within_one_product_is_refused()
    {
        var product = CreateProduct();

        var act = () => product.AddVariant("sku-1", "Third", null, 0, Now);
        act.Should().Throw<DuplicateEntityException>();
    }

    [Fact]
    public void A_soft_deleted_product_is_hidden_from_public_queries()
    {
        var product = CreateProduct();
        product.SubmitForApproval(Now);
        product.Approve(Now);
        product.SoftDelete(Now);

        product.IsPubliclyVisible.Should().BeFalse();
    }
}

public sealed class CategoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_category_cannot_be_its_own_parent()
    {
        var category = Category.Create("Phones", Slug.Create("phones"), null, null, 0, Now);

        var act = () => category.ChangeParent(category.Id, category.Id, [], Now);
        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void A_category_cannot_be_moved_under_its_own_descendant()
    {
        var parent = Category.Create("Electronics", Slug.Create("electronics"), null, null, 0, Now);
        var child = Category.Create("Audio", Slug.Create("audio"), null, parent.Id, 0, Now);

        var act = () => parent.ChangeParent(child.Id, parent.Id, [child.Id], Now);
        act.Should().Throw<BusinessRuleException>();
    }

    [Fact]
    public void A_category_with_products_can_be_deactivated_but_not_deleted()
    {
        var category = Category.Create("Electronics", Slug.Create("electronics"), null, null, 0, Now);
        category.RecalculateProductCount(3, Now);

        var act = () => category.SoftDelete(Now);
        act.Should().Throw<BusinessRuleException>();

        category.SetActive(false, Now);
        category.IsActive.Should().BeFalse();
    }

    [Fact]
    public void A_tree_keeps_parent_child_ordering()
    {
        var root = Category.Create("Electronics", Slug.Create("electronics"), null, null, 0, Now);
        var audio = Category.Create("Audio", Slug.Create("audio"), null, root.Id, 0, Now);
        var mobile = Category.Create("Mobile", Slug.Create("mobile"), null, root.Id, 0, Now);

        var tree = ApplicationTestHelper.BuildTree([root, mobile, audio]);

        tree.Should().HaveCount(1);
        tree[0].Children.Should().HaveCount(2);
    }
}

internal static class ApplicationTestHelper
{
    public static IReadOnlyList<Application.Modules.Catalog.DTOs.CategoryResponse> BuildTree(
        IReadOnlyCollection<Category> categories) =>
        Application.Modules.Catalog.Services.CategoryService.BuildTree(categories);
}
