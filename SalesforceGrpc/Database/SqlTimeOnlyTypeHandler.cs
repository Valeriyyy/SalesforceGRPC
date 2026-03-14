using Dapper;
using System.Data;

namespace SalesforceGrpc.Database;

public class SqlTimeOnlyTypeHandler : SqlMapper.TypeHandler<TimeOnly> {
    // How to set the parameter value when sending to the database
    public override void SetValue(IDbDataParameter parameter, TimeOnly time)
    {
        // For some databases, converting to TimeSpan works
        parameter.Value = time.ToTimeSpan(); 
        
        // Alternatively, for some providers, converting to a string might be necessary
        // keeping this here for when support for other dbs is added
        // parameter.Value = time.ToString(); 
    }

    // How to parse the value retrieved from the database
    public override TimeOnly Parse(object value)
    {
        // The database value might come back as a TimeSpan or DateTime depending on provider/column type
        if (value is TimeSpan timeSpan)
        {
            return TimeOnly.FromTimeSpan(timeSpan);
        }
        if (value is DateTime dateTime)
        {
            return TimeOnly.FromTimeSpan(dateTime.TimeOfDay);
        }
        
        throw new InvalidOperationException($"Cannot convert {value.GetType().FullName} to TimeOnly");
    }
}