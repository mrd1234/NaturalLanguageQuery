namespace NLQueryApp.Core.Models;

public interface IDataSourceTemplateProvider
{
    Task<List<DataSourceTemplate>> GetTemplatesAsync();
    Task<DataSourceTemplate?> GetTemplateAsync(string templateId);
}
