using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Events;
using Microsoft.AspNetCore.Hosting;
using System.Text.Json.Serialization;
using UmbracoConventions = Umbraco.Cms.Core.Constants.Conventions;
using Microsoft.Extensions.Configuration;

namespace idseefeld.de
{
    public interface IExportMediaService
    {
        string Export();
    }

    /// <summary>
    /// ExportMediaService exports all Umbraco media items into an external folder structur with item names for folders and files 
    /// as these are defined in Umbracos media section
    /// Put this file into [webroot]\App_Code folder.
    /// You might change the _exportRoot path to your needs. And you may set suitable permissions on that folder.
    /// </summary>
    public class ExportMediaService : IExportMediaService
    {
        #region Properties
        /// <summary>
        /// alternativly you can set _exportRoot in appsettings.json 
        /// eg. adding: "MediaExporter": {"ExportRootPath": "c:\\MediaExportUmbracoV9.4.2"},
        /// </summary>
        private readonly string _exportRoot = "c:\\MediaExportUmbracoV9";
        //_exportRoot needs write permission for the applicationPool user.

        private readonly ILogger _logger;
        private readonly bool exportToEmptyFolderOnly = false;
        private readonly bool exportRunOnce = false;
        private readonly char[] _invalidFilenameChars = Path.GetInvalidFileNameChars();

        private readonly IUmbracoContextFactory _umbracoContextFactory;
        private readonly IWebHostEnvironment _hostEnvironment;

        private bool exportStarted;
        private string _umbracoRoot;
        private IUmbracoContext _umbracoContext;// do NOT initialize in ctor. _umbracoContext has to be initialized by _umbracoContextFactory.EnsureUmbracoContext()!
        #endregion

        #region Ctor
        public ExportMediaService(IUmbracoContextFactory umbracoContextFactory, ILogger<ExportMediaService> logger, IWebHostEnvironment hostEnvironment, IConfiguration config)
        {
            var exporterConfig = new MediaExporterConfig();
            config.Bind(exporterConfig);
            if (!string.IsNullOrEmpty(exporterConfig.MediaExporter?.ExportRootPath))
            {
                _exportRoot = exporterConfig.MediaExporter.ExportRootPath;
            }

            _hostEnvironment = hostEnvironment;
            _umbracoContextFactory = umbracoContextFactory;
            _logger = logger;
        }
        #endregion

        public string Export()
        {
            if (exportStarted && exportRunOnce) return null;

            string resultMessage;
            using (var umbracoContextRef = _umbracoContextFactory.EnsureUmbracoContext())
            {
                _umbracoContext = umbracoContextRef.UmbracoContext;
                _umbracoRoot = _hostEnvironment.WebRootPath;

                MediaFolderAndFileInfo exportStructur;
                try
                {
                    var exportRoot = Directory.CreateDirectory(_exportRoot);
                    if (exportToEmptyFolderOnly && exportRoot.EnumerateFiles().Any())
                    {
                        resultMessage = $"Media items allready exported. For new export delete all content of: {_exportRoot}";
                        Logger(resultMessage);
                        return resultMessage;
                    }
                    var mr = _umbracoContext.Media.GetAtRoot();
                    if (mr == null)
                    {
                        resultMessage = "No Media Root found.";
                        Logger(resultMessage);
                        return resultMessage;
                    }

                    var fixedNames = new List<FixedNames>();
                    exportStructur = new MediaFolderAndFileInfo()
                    {
                        Name = "Media",
                        PathSegment = "",
                        Children = GetMediaItemsRecursive(mr, _exportRoot, fixedNames)
                    };

                    var reportJson = JsonSerializer.Serialize(exportStructur);
                    File.WriteAllText(Path.Combine(_exportRoot, "export-report.json"), reportJson);

                    if (fixedNames.Count > 0)
                    {
                        reportJson = JsonSerializer.Serialize(fixedNames);
                        File.WriteAllText(Path.Combine(_exportRoot, "export-fixednames.json"), reportJson);
                    }

                    resultMessage = "Media Section exported.";
                    Logger(resultMessage);

                    //the export should run only once per application execution.
                    exportStarted = true;
                }
                catch (Exception ex)
                {
                    Logger(ex);

                    resultMessage = $"Media Section not exported! {Environment.NewLine}{ex.Message} {Environment.NewLine}{ex.Source} {Environment.NewLine}{ex.StackTrace}";
                    File.WriteAllText(Path.Combine(_exportRoot, "export-error.json"), resultMessage);
                }
            }
            return resultMessage;
        }
        private IEnumerable<MediaFolderAndFileInfo> GetMediaItemsRecursive(IEnumerable<IPublishedContent> mr, string parentPath, List<FixedNames> fixedNames)
        {
            var rVal = new List<MediaFolderAndFileInfo>();

            foreach (var item in mr)
            {
                string umbracoFilePath = null;
                string extension = null;
                var name = GetValidFileName(item.Name);
                FixedNames fixedFilenamesOrErrors = null;
                if (!item.Name.Equals(name))
                {
                    fixedFilenamesOrErrors = new FixedNames()
                    {
                        UmbracoName = item.Name,
                        FixedName = name
                    };
                }

                var isFolder = item.ContentType.Alias == UmbracoConventions.MediaTypes.Folder;
                FocalPoint focalPoint = null;

                if (!isFolder)
                {                    
                    var umbracoFile = item.Properties
                        .FirstOrDefault(p => p.Alias == UmbracoConventions.Media.File)?
                        .GetValue()
                        .ToString()
                        .Trim();
                    if (!string.IsNullOrEmpty(umbracoFile))
                    {
                        if (item.ContentType.Alias == UmbracoConventions.MediaTypes.Image && umbracoFile.StartsWith("{"))
                        {
                            try
                            {
                                var cropperValue = JsonSerializer.Deserialize<ImageCropperValue>(umbracoFile);
                                if (cropperValue.Src != null)
                                {
                                    focalPoint = cropperValue.FocalPoint;
                                    umbracoFile = cropperValue.Src;
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger(ex);
                            }
                        }
                        var relativePath = umbracoFile.Trim('/').Replace('/', '\\');
                        umbracoFilePath = Path.Combine(_umbracoRoot, relativePath);
                        extension = Path.GetExtension(umbracoFilePath);
                    }
                }

                var path = extension == null ? name : $"{name}{extension}";
                var exportPath = Path.Combine(parentPath, path);

                var newExportElement = new MediaFolderAndFileInfo()
                {
                    Id = item.Id,
                    Name = item.Name,
                    Guid = item.Key.ToString(),
                    PathSegment = path,
                    UmbracoFilePath = umbracoFilePath,
                    ExportPath = exportPath,
                    FocalPoint = focalPoint
                };
                if (isFolder)
                {
                    Directory.CreateDirectory(exportPath);
                }
                else
                {
                    if (File.Exists(umbracoFilePath))
                    {
                        if (!File.Exists(exportPath))
                        {
                            File.Copy(umbracoFilePath, exportPath);
                        }
                    }
                    else
                    {
                        if (fixedFilenamesOrErrors == null)
                        {
                            fixedFilenamesOrErrors = new FixedNames() { UmbracoName = item.Name };
                        }
                        fixedFilenamesOrErrors.ErrorMessage = "Umbraco media item has no file source";
                        newExportElement.UmbracoFilePath = null;
                    }
                }

                if (fixedFilenamesOrErrors != null) { fixedNames.Add(fixedFilenamesOrErrors); }

                var children = item.Children.ToArray();
                if (children.Length > 0)
                {
                    newExportElement.Children = GetMediaItemsRecursive(
                            children,
                            exportPath,
                            fixedNames);
                }
                rVal.Add(newExportElement);
            }
            return rVal;
        }
        private string GetValidFileName(string fileName)
        {
            var rVal = fileName;
            foreach (var c in _invalidFilenameChars)
            {
                if (rVal.Contains(c))
                {
                    rVal = rVal.Replace(c, '_');
                }
            }
            return rVal;
        }
        private void Logger(Exception ex = null)
        {
            Logger(null, ex);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2254:Template should be a static expression", Justification = "<Pending>")]
        private void Logger(string message = null, Exception ex = null)
        {
            if (_logger == null) return;
            if (ex == null)
            {
                _logger.LogInformation(message);
            }
            else
            {
                _logger.LogError(ex.Message, ex);
            }
        }

        #region helper classes
        private class MediaExporter {
            public string ExportRootPath { get; set; }
        }
        private class MediaExporterConfig
        {
            public MediaExporter MediaExporter { get; set; }
        }
        private class ImageCropperValue
        {
            [JsonPropertyName("src")]
            public string Src { get; set; }

            [JsonPropertyName("focalPoint")]
            public FocalPoint FocalPoint { get; set; }

            [JsonPropertyName("crops")]
            public IEnumerable<Crop> Crops { get; set; }
        }
        private class Crop
        {
            public string Name { get; set; }
        }
        private class FocalPoint
        {
            [JsonPropertyName("left")]
            public float Left { get; set; }

            [JsonPropertyName("top")]
            public float Top { get; set; }
        }
        private class MediaFolderAndFileInfo
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public string PathSegment { get; set; }
            public string Guid { get; set; }
            public string ExportPath { get; set; }
            public string UmbracoFilePath { get; set; }
            public FocalPoint FocalPoint { get; set; }
            public IEnumerable<MediaFolderAndFileInfo> Children { get; set; }
        }
        private class FixedNames
        {
            public string UmbracoName { get; set; }
            public string FixedName { get; set; }
            public string ErrorMessage { get; set; }
        }
        #endregion
    }

    #region register IComponent to execute ExportMediaService.Export() on HttpApplication.BeginRequest with IComposer

    public class ExportMediaWhenAppStartedComposer : IComposer
    {
        public void Compose(IUmbracoBuilder builder)
        {
            builder.Services.AddSingleton<IExportMediaService, ExportMediaService>();
            builder.AddNotificationHandler<UmbracoRequestBeginNotification, UmbracoRequestBeginNotificationHandler>();
        }
    }
    public class UmbracoRequestBeginNotificationHandler : INotificationHandler<UmbracoRequestBeginNotification>
    {
        private readonly IExportMediaService _exportMediaService;

        public UmbracoRequestBeginNotificationHandler(IExportMediaService exportMediaService)
        {
            _exportMediaService = exportMediaService;
        }

        public void Handle(UmbracoRequestBeginNotification notification)
        {
            if (notification.UmbracoContext.OriginalRequestUrl.LocalPath == "/")
            {
                _exportMediaService.Export();
            }
        }
    }
    #endregion
}