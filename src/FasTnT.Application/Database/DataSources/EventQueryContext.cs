﻿using FasTnT.Application.Database.DataSources.Utils;
using FasTnT.Application.Services.DataSources.Utils;
using FasTnT.Domain.Enumerations;
using FasTnT.Domain.Exceptions;
using FasTnT.Domain.Model.Events;
using FasTnT.Domain.Model.Masterdata;
using FasTnT.Domain.Model.Queries;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace FasTnT.Application.Database.DataSources;

internal sealed class EventQueryContext
{
    private bool _ascending;
    private int? _skip, _take;
    private readonly List<Func<IQueryable<Event>, IQueryable<Event>>> _filters = [];
    private readonly EpcisContext _context;

    internal EventQueryContext(EpcisContext context, IEnumerable<QueryParameter> parameters)
    {
        _context = context;

        foreach (var parameter in parameters)
        {
            ParseParameter(parameter);
        }

        _filters.Add(x => _skip.HasValue ? x.Skip(_skip.Value) : x);
        _filters.Add(x => _take.HasValue ? x.Take(_take.Value) : x);
    }

    private void ParseParameter(QueryParameter param)
    {
        switch (param.Name)
        {
            // Order parameters
            case "orderBy":
                ParseOrderField(param); break;
            case "orderDirection":
                _ascending = param.AsString() == "ASC"; break;
            // Pagination parameters
            case "nextPageToken":
                _skip = Math.Max(_skip ?? 0, param.AsInt()); break;
            case "eventCountLimit" or "perPage" or "maxEventCount":
                _take = Math.Min(_take ?? int.MaxValue, param.AsInt()); break;
            // Simple filters
            case "eventType":
                Filter(x => param.Values.Select(x => x.Parse<EventType>()).Contains(x.Type)); break;
            case "GE_eventTime":
                Filter(x => x.EventTime >= param.AsDate()); break;
            case "LT_eventTime":
                Filter(x => x.EventTime < param.AsDate()); break;
            case "GE_recordTime":
                Filter(x => x.Request.RecordTime >= param.AsDate()); break;
            case "LT_recordTime":
                Filter(x => x.Request.RecordTime < param.AsDate()); break;
            case "EQ_action":
                Filter(x => param.Values.Select(x => x.Parse<EventAction>()).Contains(x.Action)); break;
            case "EQ_bizLocation":
                Filter(x => param.Values.Contains(x.BusinessLocation)); break;
            case "EQ_bizStep":
                Filter(x => param.Values.Contains(x.BusinessStep)); break;
            case "EQ_disposition":
                Filter(x => param.Values.Contains(x.Disposition)); break;
            case "EQ_eventID":
                Filter(x => param.Values.Contains(x.EventId)); break;
            case "EQ_transformationID":
                Filter(x => param.Values.Contains(x.TransformationId)); break;
            case "EQ_readPoint":
                Filter(x => param.Values.Contains(x.ReadPoint)); break;
            case "EQ_userID":
                Filter(x => param.Values.Contains(x.Request.UserId)); break;
            case "EXISTS_errorDeclaration":
                Filter(x => x.CorrectiveDeclarationTime.HasValue); break;
            case "EQ_errorReason":
                Filter(x => param.Values.Contains(x.CorrectiveReason)); break;
            case "EQ_correctiveEventID":
                Filter(x => x.CorrectiveEventIds.Any(ce => param.Values.Contains(ce.CorrectiveId))); break;
            case "GE_errorDeclarationTime":
                Filter(x => x.CorrectiveDeclarationTime >= param.AsDate()); break;
            case "LT_errorDeclarationTime":
                Filter(x => x.CorrectiveDeclarationTime < param.AsDate()); break;
            case "WD_readPoint":
                Filter(x => _context.Set<MasterdataHierarchy>().Any(h => h.Type == MasterData.ReadPoint && h.Root == x.ReadPoint && param.Values.Contains(h.Id))); break;
            case "WD_bizLocation":
                Filter(x => _context.Set<MasterdataHierarchy>().Any(h => h.Type == MasterData.Location && h.Root == x.BusinessLocation && param.Values.Contains(h.Id))); break;
            case "EQ_requestID":
                Filter(x => param.Values.Select(int.Parse).Contains(x.Request.Id)); break;
            case "EQ_captureID":
                Filter(x => param.Values.Contains(x.Request.CaptureId)); break;
            case "EQ_quantity":
                Filter(x => x.Epcs.Any(e => e.Type == EpcType.Quantity && e.Quantity == param.AsFloat())); break;
            case "GT_quantity":
                Filter(x => x.Epcs.Any(e => e.Type == EpcType.Quantity && e.Quantity > param.AsFloat())); break;
            case "GE_quantity":
                Filter(x => x.Epcs.Any(e => e.Type == EpcType.Quantity && e.Quantity >= param.AsFloat())); break;
            case "LT_quantity":
                Filter(x => x.Epcs.Any(e => e.Type == EpcType.Quantity && e.Quantity < param.AsFloat())); break;
            case "LE_quantity":
                Filter(x => x.Epcs.Any(e => e.Type == EpcType.Quantity && e.Quantity <= param.AsFloat())); break;
            // parameters introduced in EPCIS 2.0
            case "GE_startTime":
                Filter(x => x.SensorElements.Any(s => s.StartTime >= param.AsDate())); break;
            case "LT_startTime":
                Filter(x => x.SensorElements.Any(s => s.StartTime < param.AsDate())); break;
            case "GE_endTime":
                Filter(x => x.SensorElements.Any(s => s.EndTime >= param.AsDate())); break;
            case "LT_endTime":
                Filter(x => x.SensorElements.Any(s => s.EndTime < param.AsDate())); break;
            case "EQ_type":
                Filter(x => x.Reports.Any(r => r.Type == param.AsString())); break;
            case "EQ_deviceID":
                Filter(x => x.Reports.Any(r => r.DeviceId == param.AsString())); break;
            case "EQ_dataProcessingMethod":
                Filter(x => x.Reports.Any(r => param.Values.Contains(r.DataProcessingMethod))); break;
            case "EQ_microorganism":
                Filter(x => x.Reports.Any(r => param.Values.Contains(r.Microorganism))); break;
            case "EQ_chemicalSubstance":
                Filter(x => x.Reports.Any(r => param.Values.Contains(r.ChemicalSubstance))); break;
            case "EQ_bizRules":
                Filter(x => x.SensorElements.Any(s => param.Values.Contains(s.BizRules))); break;
            case "EQ_stringValue":
                Filter(x => x.Reports.Any(r => r.StringValue == param.AsString())); break;
            case "EQ_booleanValue":
                Filter(x => x.Reports.Any(r => r.BooleanValue == param.AsBool())); break;
            case "EQ_hexBinaryValue":
                Filter(x => x.Reports.Any(r => param.Values.Contains(r.HexBinaryValue))); break;
            case "EQ_uriValue":
                Filter(x => x.Reports.Any(r => param.Values.Contains(r.UriValue))); break;
            case "GE_percRank":
                Filter(x => x.Reports.Any(r => r.PercRank >= param.AsFloat())); break;
            case "LT_percRank":
                Filter(x => x.Reports.Any(r => r.PercRank < param.AsFloat())); break;
            case "EQ_persistentDisposition_set":
                ApplyPersistentDispositionFilter(param, PersistentDispositionType.Set); break;
            case "EQ_persistentDisposition_unset":
                ApplyPersistentDispositionFilter(param, PersistentDispositionType.Unset); break;
            // Family filters
            case var s when s.StartsWith("MATCH_"):
                ApplyMatchParameter(param); break;
            case var s when s.StartsWith("EQ_source_"):
                Filter(x => x.Sources.Any(s => s.Type == param.GetSimpleType() && param.Values.Contains(s.Id))); break;
            case var s when s.StartsWith("EQ_destination_"):
                Filter(x => x.Destinations.Any(d => d.Type == param.GetSimpleType() && param.Values.Contains(d.Id))); break;
            case var s when s.StartsWith("EQ_bizTransaction_"):
                Filter(x => x.Transactions.Any(t => t.Type == param.GetSimpleType() && param.Values.Contains(t.Id))); break;
            case var s when s.StartsWith("EQ_INNER_ILMD_"):
                ApplyFieldParameter(FieldType.Ilmd, true, param.InnerIlmdName(), param.InnerIlmdNamespace(), param.Values); break;
            case var s when s.StartsWith("EQ_ILMD_"):
                ApplyFieldParameter(FieldType.Ilmd, false, param.IlmdName(), param.IlmdNamespace(), param.Values); break;
            case var s when s.StartsWith("EQ_INNER_SENSORELEMENT_"):
                ApplyFieldParameter(FieldType.Sensor, true, param.InnerIlmdName(), param.InnerIlmdNamespace(), param.Values); break;
            case var s when s.StartsWith("EQ_SENSORELEMENT_"):
                ApplyFieldParameter(FieldType.Sensor, false, param.IlmdName(), param.IlmdNamespace(), param.Values); break;
            case var s when s.StartsWith("EQ_SENSORMETADATA_"):
                ApplyFieldParameter(FieldType.SensorMetadata, false, param.IlmdName(), param.IlmdNamespace(), param.Values); break;
            case var s when s.StartsWith("EQ_INNER_SENSORMETADATA_"):
                ApplyFieldParameter(FieldType.SensorMetadata, true, param.InnerIlmdName(), param.InnerIlmdNamespace(), param.Values); break;
            case var s when s.StartsWith("EQ_SENSORREPORT_"):
                ApplyFieldParameter(FieldType.SensorReport, false, param.IlmdName(), param.IlmdNamespace(), param.Values); break;
            case var s when s.StartsWith("EQ_INNER_SENSORREPORT_"):
                ApplyFieldParameter(FieldType.SensorReport, true, param.InnerIlmdName(), param.InnerIlmdNamespace(), param.Values); break;
            case var s when s.StartsWith("EXISTS_INNER_ILMD_"):
                ApplyFieldParameter(FieldType.Ilmd, true, param.InnerIlmdName(), param.InnerIlmdNamespace(), []); break;
            case var s when s.StartsWith("EXISTS_ILMD_"):
                ApplyFieldParameter(FieldType.Ilmd, false, param.IlmdName(), param.IlmdNamespace(), []); break;
            case var s when s.StartsWith("EXISTS_INNER_"):
                ApplyFieldParameter(FieldType.CustomField, true, param.InnerFieldName(), param.InnerFieldNamespace(), []); break;
            case var s when s.StartsWith("EXISTS_"):
                ApplyFieldParameter(FieldType.CustomField, false, param.FieldName(), param.FieldNamespace(), []); break;
            case var s when s.StartsWith("EQ_INNER_"):
                ApplyFieldParameter(FieldType.CustomField, true, param.InnerFieldName(), param.InnerFieldNamespace(), param.Values); break;
            case var s when s.StartsWith("EQ_value_"):
                ApplyReportUomParameter(param.Values.Select(float.Parse), param.ReportFieldUom()); break;
            case var s when s.StartsWith("EQ_"):
                ApplyFieldParameter(FieldType.CustomField, false, param.FieldName(), param.FieldNamespace(), param.Values); break;
            case var s when s.StartsWith("EQATTR_"):
                ApplyMasterdataAttributeParameter(param.MasterdataType(), param.AttributeName(), param.Values); break;
            case var s when s.StartsWith("HASATTR_"):
                ApplyMasterdataAttributeParameter(param.MasterdataType(), param.AttributeName(), []); break;
            // Regex filters (Date/Numeric value comparison)
            case var r when Regexs.InnerIlmd().IsMatch(r):
                ApplyComparison(param, FieldType.Ilmd, param.InnerIlmdNamespace(), param.InnerIlmdName(), true); break;
            case var r when Regexs.Ilmd().IsMatch(r):
                ApplyComparison(param, FieldType.Ilmd, param.IlmdNamespace(), param.IlmdName(), false); break;
            case var r when Regexs.SensorFilter().IsMatch(r):
                ApplyComparison(param, param.SensorType(), param.SensorFieldNamespace(), param.SensorFieldName(), false); break;
            case var r when Regexs.InnerSensorFilter().IsMatch(r):
                ApplyComparison(param, param.InnerSensorType(), param.InnerSensorFieldNamespace(), param.InnerSensorFieldName(), true); break;
            case var r when Regexs.InnerField().IsMatch(r):
                ApplyComparison(param, FieldType.Extension, param.InnerFieldNamespace(), param.InnerFieldName(), true); break;
            case var r when Regexs.UoMField().IsMatch(r):
                ApplyUomComparison(param); break;
            case var r when Regexs.Field().IsMatch(r):
                ApplyComparison(param, FieldType.Extension, param.FieldNamespace(), param.FieldName(), false); break;
            // Any other case is an unknown parameter and should raise a QueryParameter Exception
            default:
                throw new EpcisException(ExceptionType.QueryParameterException, $"Parameter is not implemented: {param.Name}");
        }
    }

    public IQueryable<Event> ApplyTo(IQueryable<Event> query)
    {
        return _filters.Aggregate(query, (query, filter) => filter(query));
    }

    private void ApplyMasterdataAttributeParameter(string field, string attributeName, string[] values)
    {
        var filter = (Expression<Func<MasterDataAttribute, bool>>)(a => a.Id == attributeName);

        if (values.Length > 0)
        {
            filter = filter.AndAlso(a => values.Contains(a.Value));
        }

        switch (field)
        {
            case "bizLocation":
                Filter(e => _context.Set<MasterData>().Any(p => p.Type == MasterData.Location && p.Id == e.BusinessLocation && p.Attributes.AsQueryable().Any(filter))); break;
            case "readPoint":
                Filter(e => _context.Set<MasterData>().Any(p => p.Type == MasterData.ReadPoint && p.Id == e.ReadPoint && p.Attributes.AsQueryable().Any(filter))); break;
            default:
                throw new EpcisException(ExceptionType.QueryParameterException, $"Invalid masterdata field: {field}");
        }
    }

    private void ParseOrderField(QueryParameter param)
    {
        switch (param.AsString())
        {
            case "eventTime":
                _filters.Add(x => _ascending ? x.OrderBy(x => x.EventTime) : x.OrderByDescending(x => x.EventTime)); break;
            case "recordTime":
                _filters.Add(x => _ascending ? x.OrderBy(x => x.Request.RecordTime) : x.OrderByDescending(x => x.Request.RecordTime)); break;
            default:
                throw new EpcisException(ExceptionType.QueryParameterException, $"Invalid order field: {param.AsString()}");
        }
    }

    private void ApplyFieldParameter(FieldType type, bool inner, string name, string ns, string[] values)
    {
        var filter = (Expression<Func<Field, bool>>)(f => f.Type == type && f.ParentIndex.HasValue == inner && f.Name == name && f.Namespace == ns);

        if (values.Length > 0)
        {
            filter = filter.AndAlso(f => values.Contains(f.TextValue));
        }

        Filter(x => x.Fields.AsQueryable().Any(filter));
    }

    private void ApplyReportUomParameter(IEnumerable<float> values, string uom)
    {
        Filter(x => x.Reports.Any(r => r.UnitOfMeasure == uom && r.Value.HasValue && values.Contains(r.Value.Value)));
    }

    private void ApplyComparison(QueryParameter param, FieldType type, string ns, string name, bool inner)
    {
        var customFieldPredicate = (Expression<Func<Field, bool>>)(f => f.Type == type && f.Name == name && f.Namespace == ns && f.ParentIndex.HasValue == inner);
        var fieldValuePredicate = param.Compare<Field>(x => param.IsDateTime() ? x.DateValue : x.NumericValue);

        Filter(x => x.Fields.AsQueryable().Any(customFieldPredicate.AndAlso(fieldValuePredicate)));
    }

    private void ApplyMatchParameter(QueryParameter param)
    {
        var epcType = param.GetMatchEpcTypes();
        var values = param.Values.Select(p => p.Replace("*", "%"));
        var typePredicate = (Expression<Func<Epc, bool>>)(e => epcType.Contains(e.Type));
        var likePredicate = values.Aggregate(False<Epc>(), (expr, value) => expr.OrElse(e => EF.Functions.Like(e.Id, value)));

        Filter(x => x.Epcs.AsQueryable().Any(typePredicate.AndAlso(likePredicate)));
    }

    private void ApplyPersistentDispositionFilter(QueryParameter param, PersistentDispositionType type)
    {
        var typePredicate = (Expression<Func<PersistentDisposition, bool>>)(x => x.Type == type);
        var anyPredicate = param.Values.Aggregate(False<PersistentDisposition>(), (expr, value) => expr.OrElse(e => e.Id == value));

        Filter(x => x.PersistentDispositions.AsQueryable().Any(typePredicate.AndAlso(anyPredicate)));
    }

    private void ApplyUomComparison(QueryParameter param)
    {
        var customFieldPredicate = (Expression<Func<SensorReport, bool>>)(r => r.UnitOfMeasure == param.ReportFieldUom());
        var fieldValuePredicate = param.Compare<SensorReport>(r => EF.Property<float?>(r, param.ReportField()));

        Filter(x => x.Reports.AsQueryable().Any(customFieldPredicate.AndAlso(fieldValuePredicate)));
    }

    private void Filter(Expression<Func<Event, bool>> expression)
    {
        _filters.Add(evt => evt.Where(expression));
    }

    private static Expression<Func<T, bool>> False<T>() => _ => false;
}