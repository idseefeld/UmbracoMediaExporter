﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.Helpers;
using Umbraco.Core;
using Umbraco.Core.Composing;
using Umbraco.Core.Logging;
using Umbraco.Core.Models.PublishedContent;
using Umbraco.Web;

namespace idseefeld.de
{
    /// <summary>
    /// ExportMediaService exports all Umbraco media items into an external folder structur with item names for folders and files 
    /// as these are defined in Umbracos media section
    /// Put this file into [webroot]\App_Code folder.
    /// You might change the _exportRoot path to your needs. And you may set suitable permissions on that folder.
    /// </summary>
    public class ExportMediaService
    {
        #region properties
        private readonly string _exportRoot = "c:\\MediaExportUmbraco8";
        //_exportRoot needs write permission for the applicationPool user.

        private readonly string _umbracoRoot;
        private readonly ILogger _logger;
        private readonly bool exportToEmptyFolderOnly = true;
        private readonly UmbracoContext _umbracoContext;
        private readonly char[] _invalidFilenameChars = Path.GetInvalidFileNameChars();

        private readonly IUmbracoContextFactory _umbracoContextFactory;
        #endregion

        #region ctors
        public ExportMediaService(UmbracoContext umbracoContext)
        {
            _umbracoContext = umbracoContext;
            _umbracoRoot = _umbracoContext.HttpContext.Server.MapPath("/");
        }
        public ExportMediaService(IUmbracoContextFactory umbracoContextFactory, ILogger logger)
        {
            _logger = logger;
            _umbracoContextFactory = umbracoContextFactory;
            using (var umbracoContextRef = _umbracoContextFactory.EnsureUmbracoContext())
            {
                _umbracoContext = umbracoContextRef.UmbracoContext;
                _umbracoRoot = _umbracoContext.HttpContext.Server.MapPath("/");
            }
        }
        #endregion

        public string Export()
        {
            string resultMessage;
            MediaFolderAndFileInfo exportStructur;
            try
            {
                var exportRoot = Directory.CreateDirectory(_exportRoot);
                if (exportToEmptyFolderOnly && exportRoot.EnumerateFiles().Count() > 0)
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

                var reportJson = Json.Encode(exportStructur);
                File.WriteAllText(Path.Combine(_exportRoot, "export-report.json"), reportJson);

                if (fixedNames.Count > 0)
                {
                    reportJson = Json.Encode(fixedNames);
                    File.WriteAllText(Path.Combine(_exportRoot, "export-fixednames.json"), reportJson);
                }

                resultMessage = "Media Section exported.";
                Logger(resultMessage);
            }
            catch (Exception ex)
            {
                Logger(ex);

                resultMessage = $"Media Section not exported! {Environment.NewLine}{ex.Message} {Environment.NewLine}{ex.Source} {Environment.NewLine}{ex.StackTrace}";
                System.IO.File.WriteAllText(Path.Combine(_exportRoot, "export-error.json"), resultMessage);
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

                var isFolder = item.ContentType.Alias == "Folder";
                FocalPoint focalPoint = null;

                if (!isFolder)
                {
                    var umbracoFile = item.Properties
                        .FirstOrDefault(p => p.Alias == "umbracoFile")?
                        .GetValue(umbracoFilePath)
                        .ToString();
                    if (!string.IsNullOrEmpty(umbracoFile))
                    {
                        if (item.ContentType.Alias == "Image")
                        {
                            try
                            {
                                var cropperValue = Json.Decode<ImageCropperValue>(umbracoFile);
                                if (cropperValue.Src != null)
                                {
                                    focalPoint = cropperValue.FocalPoint;
                                    umbracoFile = cropperValue.Src;
                                }
                            }
                            catch (Exception ex)
                            {
                                if (_logger != null) _logger.Error<ExportMediaService>(ex);
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
        private void Logger(string message = null, Exception ex = null)
        {
            if (_logger == null) return;

            if (ex == null)
            {
                _logger.Debug<ExportMediaService>(message);
            }
            else
            {
                _logger.Error<ExportMediaService>(ex);
            }
        }

        #region helper classes

        private class ImageCropperValue
        {
            public string Src { get; set; }
            public FocalPoint FocalPoint { get; set; }
        }
        private class FocalPoint
        {
            public float Left { get; set; }
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
        public void Compose(Composition composition)
        {
            composition.Components().Append<ExportMediaWhenAppStartedComponent>();
        }
    }

    public class ExportMediaWhenAppStartedComponent : IComponent
    {
        private readonly ILogger _logger;
        private readonly IUmbracoContextFactory _contextFactory;

        public ExportMediaWhenAppStartedComponent(
            IUmbracoContextFactory contextFactory,
            ILogger logger)
        {
            _logger = logger;
            _contextFactory = contextFactory;
        }

        public void Initialize()
        {
            UmbracoApplication.ApplicationInit += UmbracoApplication_ApplicationInit;
        }

        private void UmbracoApplication_ApplicationInit(object sender, EventArgs e)
        {
            if (!(sender is HttpApplication app)) return;

            app.BeginRequest += App_BeginRequest;
        }

        private void App_BeginRequest(object sender, EventArgs e)
        {
            var exporter = new ExportMediaService(_contextFactory, _logger);
            var result = exporter.Export();
        }

        public void Terminate()
        {
            UmbracoApplication.ApplicationInit -= UmbracoApplication_ApplicationInit;
        }
    }
    #endregion
}