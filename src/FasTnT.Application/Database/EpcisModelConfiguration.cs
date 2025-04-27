﻿using FasTnT.Application.Database;
using FasTnT.Domain.Model;
using FasTnT.Domain.Model.Events;
using FasTnT.Domain.Model.Masterdata;
using FasTnT.Domain.Model.Queries;
using FasTnT.Domain.Model.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.Text.Json;
using static System.Text.Json.JsonSerializer;

namespace FasTnT.Application.Database;

public static class EpcisModelConfiguration
{
    const string Epcis = nameof(Epcis);
    const string Sbdh = nameof(Sbdh);
    const string Cbv = nameof(Cbv);
    const string Subscriptions = nameof(Subscriptions);
    const string Queries = nameof(Queries);

    public static void Apply(ModelBuilder modelBuilder)
    {
        var request = modelBuilder.Entity<Request>();
        request.ToTable(nameof(Request), Epcis);
        request.HasKey(x => x.Id);
        request.Property(x => x.Id).ValueGeneratedOnAdd();
        request.Property(x => x.UserId).HasMaxLength(50);
        request.Property(x => x.DocumentTime).IsRequired();
        request.Property(x => x.RecordTime).IsRequired();
        request.Property(x => x.CaptureId).IsRequired().HasMaxLength(256);
        request.Property(x => x.SchemaVersion).IsRequired().HasMaxLength(10);
        request.HasMany(x => x.Events).WithOne(x => x.Request).HasForeignKey("RequestId").OnDelete(DeleteBehavior.Cascade);
        request.HasMany(x => x.Masterdata).WithOne(x => x.Request).HasForeignKey("RequestId").OnDelete(DeleteBehavior.Cascade);
        request.HasOne(x => x.StandardBusinessHeader).WithOne().HasForeignKey<StandardBusinessHeader>("RequestId");
        request.HasOne(x => x.SubscriptionCallback).WithOne().HasForeignKey<SubscriptionCallback>("RequestId");

        var subscriptionCallback = modelBuilder.Entity<SubscriptionCallback>();
        subscriptionCallback.ToTable(nameof(SubscriptionCallback), Epcis);
        subscriptionCallback.Property<int>("RequestId");
        subscriptionCallback.HasKey("RequestId");
        subscriptionCallback.HasOne(x => x.Request).WithOne(x => x.SubscriptionCallback).HasForeignKey<SubscriptionCallback>("RequestId");
        subscriptionCallback.Property(x => x.CallbackType).IsRequired().HasConversion<short>();
        subscriptionCallback.Property(x => x.Reason).IsRequired(false).HasMaxLength(256);
        subscriptionCallback.Property(x => x.SubscriptionId).HasColumnName("SubscriptionId").IsRequired().HasMaxLength(256);

        var sbdh = modelBuilder.Entity<StandardBusinessHeader>();
        sbdh.ToTable(nameof(StandardBusinessHeader), Sbdh);
        sbdh.HasKey("RequestId");
        sbdh.Property<int>("RequestId");
        sbdh.Property(x => x.Version).HasMaxLength(256).IsRequired();
        sbdh.Property(x => x.Standard).HasMaxLength(256).IsRequired();
        sbdh.Property(x => x.TypeVersion).HasMaxLength(256).IsRequired();
        sbdh.Property(x => x.InstanceIdentifier).HasMaxLength(256).IsRequired();
        sbdh.Property(x => x.Type).HasMaxLength(256).IsRequired();
        sbdh.Property(x => x.CreationDateTime).IsRequired(false);
        sbdh.OwnsMany(x => x.ContactInformations, c =>
        {
            c.ToTable(nameof(ContactInformation), Sbdh);
            c.Property<int>("RequestId");
            c.HasKey("RequestId", nameof(ContactInformation.Type), nameof(ContactInformation.Identifier));
            c.Property(x => x.Type).HasMaxLength(256).HasConversion<short>().IsRequired();
            c.Property(x => x.Identifier).HasMaxLength(256).IsRequired();
            c.Property(x => x.Contact).HasMaxLength(256).IsRequired(false);
            c.Property(x => x.EmailAddress).HasMaxLength(256).IsRequired(false);
            c.Property(x => x.FaxNumber).HasMaxLength(256).IsRequired(false);
            c.Property(x => x.TelephoneNumber).HasMaxLength(256).IsRequired(false);
            c.Property(x => x.ContactTypeIdentifier).HasMaxLength(256).IsRequired(false);
        });

        var masterData = modelBuilder.Entity<MasterData>();
        masterData.ToView("CurrentMasterdata", Cbv);
        masterData.ToTable(nameof(MasterData), Cbv);
        masterData.Property<int>("RequestId");
        masterData.HasKey("RequestId", nameof(MasterData.Index));
        masterData.Property(x => x.Index).ValueGeneratedNever();
        masterData.Property(x => x.Type).HasMaxLength(256).IsRequired();
        masterData.Property(x => x.Id).HasMaxLength(256).IsRequired();
        masterData.OwnsMany(x => x.Attributes, mdAttribute =>
        {
            mdAttribute.ToTable(nameof(MasterDataAttribute), Cbv);
            mdAttribute.Property<int>("RequestId");
            mdAttribute.Property<int>("MasterDataIndex");
            mdAttribute.HasKey("RequestId", "MasterDataIndex", nameof(MasterDataAttribute.Index));
            mdAttribute.Property(x => x.Id).HasMaxLength(256).IsRequired();
            mdAttribute.Property(x => x.Value).HasMaxLength(256).IsRequired();
            mdAttribute.Property(x => x.Index).HasMaxLength(256).IsRequired().ValueGeneratedNever();
            mdAttribute.WithOwner().HasForeignKey("RequestId", "MasterDataIndex");
            mdAttribute.OwnsMany(x => x.Fields, field =>
            {
                field.ToTable(nameof(MasterDataField), Cbv);
                field.Property<int>("RequestId");
                field.Property<int>("MasterDataIndex");
                field.Property<int>("AttributeIndex").HasMaxLength(256).IsRequired().ValueGeneratedNever();
                field.HasKey("RequestId", "MasterDataIndex", "AttributeIndex", nameof(MasterDataField.Index));
                field.Property(x => x.Index).HasMaxLength(256).IsRequired().ValueGeneratedNever();
                field.Property(x => x.ParentIndex).HasMaxLength(256).IsRequired(false).ValueGeneratedNever();
                field.Property(x => x.Namespace).HasMaxLength(256).IsRequired();
                field.Property(x => x.Name).HasMaxLength(256).IsRequired();
                field.Property(x => x.Value).HasMaxLength(256).IsRequired(false);
                field.WithOwner().HasForeignKey("RequestId", "MasterDataIndex", "AttributeIndex");
            });
        });
        masterData.HasMany(x => x.Children).WithOne(x => x.MasterData).HasForeignKey("MasterDataRequestId", "MasterDataIndex");
        masterData.Navigation(x => x.Attributes).AutoInclude(false);

        var mdChildren = modelBuilder.Entity<MasterDataChildren>();
        mdChildren.ToTable(nameof(MasterDataChildren), Cbv);
        mdChildren.Property<int>("MasterDataRequestId").HasMaxLength(256);
        mdChildren.Property<int>("MasterDataIndex");
        mdChildren.HasKey("MasterDataRequestId", "MasterDataIndex", "ChildrenId");
        mdChildren.HasOne(x => x.MasterData).WithMany(x => x.Children).HasForeignKey("MasterDataRequestId", "MasterDataIndex");
        mdChildren.Property(x => x.ChildrenId).HasMaxLength(256);

        var mdHierarchy = modelBuilder.Entity<MasterdataHierarchy>();
        mdHierarchy.ToView(nameof(MasterdataHierarchy), Cbv);
        mdHierarchy.HasNoKey();
        mdHierarchy.Property(x => x.Root).IsRequired();
        mdHierarchy.Property(x => x.Id).IsRequired();
        mdHierarchy.Property(x => x.Type).IsRequired();

        var evt = modelBuilder.Entity<Event>();
        evt.ToTable(nameof(Event), Epcis);
        evt.HasKey(x => x.Id);
        evt.Property(x => x.Id).ValueGeneratedOnAdd();
        evt.Property(x => x.Type).IsRequired().HasConversion<short>();
        evt.Property(x => x.EventTime).IsRequired();
        evt.Property(x => x.EventTimeZoneOffset).IsRequired().HasConversion(x => x.Value, x => x);
        evt.Property(x => x.Action).IsRequired().HasConversion<short>();
        evt.Property(x => x.CertificationInfo).HasMaxLength(256).IsRequired(false);
        evt.Property(x => x.EventId).HasMaxLength(256).IsRequired(false);
        evt.Property(x => x.ReadPoint).HasMaxLength(256).IsRequired(false);
        evt.Property(x => x.BusinessLocation).HasMaxLength(256).IsRequired(false);
        evt.Property(x => x.BusinessStep).HasMaxLength(256).IsRequired(false);
        evt.Property(x => x.Disposition).HasMaxLength(256).IsRequired(false);
        evt.Property(x => x.TransformationId).HasMaxLength(256).IsRequired(false);
        evt.Property(x => x.CorrectiveDeclarationTime).IsRequired(false);
        evt.Property(x => x.CorrectiveReason).HasMaxLength(256).IsRequired(false);
        evt.HasOne(x => x.Request).WithMany(x => x.Events).HasForeignKey("RequestId");
        evt.Navigation(e => e.Request).AutoInclude();
        evt.OwnsMany(x => x.Epcs, c =>
        {
            c.ToTable(nameof(Epc), Epcis);
            c.Property<int>("EventId");
            c.WithOwner().HasForeignKey("EventId");
            c.HasKey("EventId", nameof(Epc.Type), nameof(Epc.Id));
            c.Property(x => x.Type).IsRequired().HasConversion<short>();
            c.Property(x => x.Id).HasMaxLength(256).IsRequired();
            c.Property(x => x.Quantity).IsRequired(false);
            c.Property(x => x.UnitOfMeasure).IsRequired(false).HasMaxLength(10);
        });
        evt.OwnsMany(x => x.Sources, c =>
        {
            c.ToTable(nameof(Source), Epcis);
            c.Property<int>("EventId");
            c.WithOwner().HasForeignKey("EventId");
            c.HasKey("EventId", nameof(Source.Type), nameof(Source.Id));
            c.Property(x => x.Type).HasMaxLength(256).IsRequired();
            c.Property(x => x.Id).HasMaxLength(256).IsRequired();
        });
        evt.OwnsMany(x => x.Destinations, c =>
        {
            c.ToTable(nameof(Destination), Epcis);
            c.Property<int>("EventId");
            c.WithOwner().HasForeignKey("EventId");
            c.HasKey("EventId", nameof(Destination.Type), nameof(Destination.Id));
            c.Property(x => x.Type).HasMaxLength(256).IsRequired();
            c.Property(x => x.Id).HasMaxLength(256).IsRequired();
        });
        evt.OwnsMany(x => x.Transactions, c =>
        {
            c.ToTable(nameof(BusinessTransaction), Epcis);
            c.Property<int>("EventId");
            c.WithOwner().HasForeignKey("EventId");
            c.HasKey("EventId", nameof(BusinessTransaction.Type), nameof(BusinessTransaction.Id));
            c.Property(x => x.Type).HasMaxLength(256).IsRequired();
            c.Property(x => x.Id).HasMaxLength(256).IsRequired();
        });
        evt.OwnsMany(x => x.PersistentDispositions, c =>
        {
            c.ToTable(nameof(PersistentDisposition), Epcis);
            c.Property<int>("EventId");
            c.WithOwner().HasForeignKey("EventId");
            c.HasKey("EventId", nameof(PersistentDisposition.Type), nameof(PersistentDisposition.Id));
            c.Property(x => x.Type).HasConversion<short>().IsRequired();
            c.Property(x => x.Id).HasMaxLength(256).IsRequired();
        });
        evt.OwnsMany(x => x.SensorElements, c =>
        {
            c.ToTable(nameof(SensorElement), Epcis);
            c.Property<int>("EventId");
            c.WithOwner().HasForeignKey("EventId");
            c.HasKey("EventId", nameof(SensorElement.Index));
            c.Property(x => x.Index).IsRequired().ValueGeneratedNever();
            c.Property(x => x.DeviceMetadata).HasMaxLength(256);
            c.Property(x => x.RawData).HasMaxLength(2048);
            c.Property(x => x.DataProcessingMethod).HasMaxLength(256);
            c.Property(x => x.DeviceMetadata).HasMaxLength(256);
            c.Property(x => x.BizRules).HasMaxLength(256);
            c.Property(x => x.DeviceId).HasMaxLength(256);
            c.Property(x => x.DeviceMetadata).HasMaxLength(256);
        });
        evt.OwnsMany(x => x.Reports, c =>
        {
            c.ToTable(nameof(SensorReport), Epcis);
            c.Property<int>("EventId");
            c.WithOwner().HasForeignKey("EventId");
            c.HasKey("EventId", nameof(SensorReport.Index));
            c.Property(x => x.Index).IsRequired().ValueGeneratedNever();
            c.Property(x => x.SensorIndex).IsRequired().ValueGeneratedNever();
            c.Property(x => x.DataProcessingMethod).HasMaxLength(256);
            c.Property(x => x.Type).HasMaxLength(256);
            c.Property(x => x.HexBinaryValue).HasMaxLength(256);
            c.Property(x => x.DeviceMetadata).HasMaxLength(256);
            c.Property(x => x.ChemicalSubstance).HasMaxLength(256);
            c.Property(x => x.Component).HasMaxLength(256);
            c.Property(x => x.DeviceId).HasMaxLength(256);
            c.Property(x => x.DeviceMetadata).HasMaxLength(256);
            c.Property(x => x.Microorganism).HasMaxLength(256);
            c.Property(x => x.RawData).HasMaxLength(2048);
            c.Property(x => x.StringValue).HasMaxLength(2048);
            c.Property(x => x.Type).HasMaxLength(256);
            c.Property(x => x.UnitOfMeasure).HasMaxLength(256);
            c.Property(x => x.UriValue).HasMaxLength(2048);
            c.Property(x => x.CoordinateReferenceSystem).HasMaxLength(256);
        });
        evt.OwnsMany(x => x.Fields, c =>
        {
            c.ToTable(nameof(Field), Epcis);
            c.Property<int>("EventId");
            c.WithOwner().HasForeignKey("EventId");
            c.HasKey("EventId", nameof(Field.Index));
            c.Property(x => x.Index).IsRequired().ValueGeneratedNever();
            c.Property(x => x.Type).HasConversion<short>().IsRequired();
            c.Property(x => x.Name).HasMaxLength(256).IsRequired();
            c.Property(x => x.Namespace).HasMaxLength(256).IsRequired(false);
            c.Property(x => x.TextValue).HasMaxLength(256).IsRequired(false);
            c.Property(x => x.NumericValue).IsRequired(false);
            c.Property(x => x.DateValue).IsRequired(false);
        });
        evt.OwnsMany(x => x.CorrectiveEventIds, c =>
        {
            c.ToTable(nameof(CorrectiveEventId), Epcis);
            c.Property<int>("EventId");
            c.WithOwner().HasForeignKey("EventId");
            c.HasKey("EventId", nameof(CorrectiveEventId.CorrectiveId));
            c.Property(x => x.CorrectiveId).IsRequired().HasMaxLength(256);
        });

        var subscription = modelBuilder.Entity<Subscription>();
        subscription.HasKey(x => x.Id);
        subscription.Property(x => x.Id).ValueGeneratedOnAdd();
        subscription.ToTable(nameof(Subscription), Subscriptions);
        subscription.Property(x => x.Name).IsRequired().HasMaxLength(256);
        subscription.Property(x => x.QueryName).IsRequired().HasMaxLength(256);
        subscription.Property(x => x.Destination).IsRequired().HasMaxLength(2048);
        subscription.Property(x => x.InitialRecordTime).IsRequired();
        subscription.Property(x => x.LastExecutedTime).IsRequired();
        subscription.Property(x => x.ReportIfEmpty).IsRequired();
        subscription.Property(x => x.Trigger).IsRequired(false).HasMaxLength(256);
        subscription.Property(x => x.SignatureToken).IsRequired(false).HasMaxLength(256);
        subscription.Property(x => x.FormatterName).IsRequired().HasMaxLength(30);
        subscription.Property(x => x.BufferRequestIds).HasJsonArrayConversion();
        subscription.OwnsOne(x => x.Schedule, c =>
        {
            c.ToTable(nameof(SubscriptionSchedule), Subscriptions);
            c.Property<int>("SubscriptionId");
            c.HasKey("SubscriptionId");
            c.Property(x => x.Second).HasMaxLength(256).IsRequired(false);
            c.Property(x => x.Minute).HasMaxLength(256).IsRequired(false);
            c.Property(x => x.Hour).HasMaxLength(256).IsRequired(false);
            c.Property(x => x.DayOfWeek).HasMaxLength(256).IsRequired(false);
            c.Property(x => x.DayOfMonth).HasMaxLength(256).IsRequired(false);
            c.Property(x => x.Month).HasMaxLength(256).IsRequired(false);
        });
        subscription.OwnsMany(x => x.Parameters, c =>
        {
            c.ToTable("SubscriptionParameter", Subscriptions);
            c.WithOwner().HasForeignKey("SubscriptionId");
            c.HasKey("SubscriptionId", nameof(QueryParameter.Name));
            c.Property(x => x.Values).IsRequired(false).HasJsonArrayConversion();
            c.Property(x => x.Name).HasMaxLength(256).IsRequired();
        });
        subscription.HasIndex(x => x.Name).IsUnique();

        var storedQuery = modelBuilder.Entity<StoredQuery>();
        storedQuery.ToTable(nameof(StoredQuery), Queries);
        storedQuery.HasKey(x => x.Id);
        storedQuery.Property(x => x.Id).ValueGeneratedOnAdd();
        storedQuery.Property(x => x.Name).IsRequired().HasMaxLength(256);
        storedQuery.Property(x => x.UserId).IsRequired(false).HasMaxLength(50);
        storedQuery.OwnsMany(x => x.Parameters, c =>
        {
            c.ToTable("StoredQueryParameter", Queries);
            c.WithOwner().HasForeignKey("QueryId");
            c.HasKey("QueryId", nameof(QueryParameter.Name));
            c.Property(x => x.Values).IsRequired(false).HasJsonArrayConversion();
            c.Property(x => x.Name).HasMaxLength(256).IsRequired();
        });
        storedQuery.HasIndex(x => x.Name).IsUnique();
    }

    private static void HasJsonArrayConversion<T>(this PropertyBuilder<T[]> builder, JsonSerializerOptions options = default)
    {
        var converter = new ValueConverter<T[], string>(v => Serialize(v, options), v => Deserialize<T[]>(v, options));
        var comparer = new ValueComparer<T[]>((l, r) => Equals(l, r), v => HashCode.Combine(v), v => v);

        builder.HasConversion(converter);
        builder.Metadata.SetValueConverter(converter);
        builder.Metadata.SetValueComparer(comparer);
    }
}
