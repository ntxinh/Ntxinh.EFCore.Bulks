using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;

namespace Ntxinh.EFCore.Bulks;

public async Task QuerySpMultipleAsync(
    this DbContext _dbContext,
    string storedProcedureName,
    object parameters = null,
    IDbContextTransaction transaction = null,
    int? commandTimeout = null,
    Func<MultiResultReader, Task> readerFunc = null,
    CancellationToken cancellationToken = default)
{
    if (string.IsNullOrWhiteSpace(storedProcedureName))
    {
        throw new ArgumentException("Stored procedure name cannot be null or empty.", nameof(storedProcedureName));
    }

    var connection = _dbContext.Database.GetDbConnection();
    if (connection.State != ConnectionState.Open)
    {
        await connection.OpenAsync(cancellationToken);
    }

    try
    {
        using var command = connection.CreateCommand();
        command.CommandText = storedProcedureName;
        command.CommandType = CommandType.StoredProcedure;
        command.CommandTimeout = commandTimeout ?? connection.ConnectionTimeout;

        if (transaction != null)
        {
            command.Transaction = transaction.GetDbTransaction();
        }

        if (parameters != null)
        {
            var sqlParameters = CreateParametersFromObject(parameters, command);
            command.Parameters.AddRange(sqlParameters);
        }

        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (readerFunc != null)
        {
            var multiReader = new MultiResultReader(reader);
            await readerFunc(multiReader);
        }
    }
    finally
    {
        if (_dbContext.Database.GetDbConnection().State == ConnectionState.Open
            && _dbContext.Database.GetDbConnection() == connection)
        {
            await connection.CloseAsync();
        }
    }
}

/// <summary>
/// Converts an object to a collection of DbParameter for the stored procedure.
/// </summary>
public static DbParameter[] CreateParametersFromObject(object parameters, DbCommand command)
{
    if (parameters == null)
    {
        return Array.Empty<DbParameter>();
    }

    var paramList = new List<DbParameter>();
    foreach (var prop in parameters.GetType().GetProperties())
    {
        var param = command.CreateParameter();
        // param.ParameterName = $"@p_{prop.Name}";
        param.ParameterName = $"@{prop.Name}";
        param.Value = prop.GetValue(parameters) ?? DBNull.Value;
        paramList.Add(param);
    }

    return paramList.ToArray();
}

// How to use
/* IEnumerable<Users> users = null;
IEnumerable<Roles> roles = null;
var parameters = new
{
    userIds = string.Join(",", userIds),
    roleIds = string.Join(",", roleIds),
};
await _dbContext.QuerySpMultipleAsync
(
    storedProcedureName: "usp_GetUsersAndRoles",
    parameters: parameters,
    readerFunc: async (multi) =>
    {
        users = (await multi.ReadAsync<Users>()).ToList();
        await multi.NextResultAsync();
        roles = (await multi.ReadAsync<Roles>()).ToList();
    }
); */
