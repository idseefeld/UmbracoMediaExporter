using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.Helpers;
using Umbraco.Core;
using Umbraco.Core.Composing;
using Umbraco.Core.Logging;
using Umbraco.Core.Models;
using Umbraco.Core.Services;
using Umbraco.Web;

namespace idseefeld.de
{
    /// <summary>
    /// ExportMedia exports all Umbraco media items into an external folder structur with item names for folders and files 
    /// as these are defined in Umbracos media section
    /// Put this file into [webroot]\App_Code folder.
    /// You might change the _exportRoot path to your needs. And you may set suitable permissions on that folder.
    /// </summary>
    public class ExportMedia
    {
        private readonly string _exportRoot = "c:\\MediaExportUmbraco8";
        //_exportRoot needs write permission for the applicationPool user.

        private readonly string _umbracoRoot;
        private readonly IMediaService _mediaService;
        private readonly char[] _invalidFilenameChars;
        private readonly ILogger _logger;
        private readonly bool exportToEmptyFolderOnly = false;
        public ExportMedia(IMediaService mediaService, string webRoot, ILogger logger)
        {
            _mediaService = mediaService;
            _invalidFilenameChars = Path.GetInvalidFileNameChars();
            _umbracoRoot = webRoot;
            _logger = logger;

            Export();
        }
        public void Export()
        {
            MediaFolderAndFileInfo exportStructur = null;
            try
            {
                var exportRoot = Directory.CreateDirectory(_exportRoot);
                if (exportToEmptyFolderOnly && exportRoot.EnumerateFiles().Count() > 0)
                {
                    _logger.Debug<ExportMedia>($"Media items allready exported. For new export delete all content of: {_exportRoot}");
                    return;
                }

                var mr = _mediaService.GetRootMedia();
                if (mr == null)
                {
                    _logger.Debug<ExportMedia>("No Media Root found.");
                    return;
                }

                var fixedNames = new List<FixedNames>();
                exportStructur = new MediaFolderAndFileInfo()
                {
                    Name = "Media",
                    PathSegment = "",
                    Children = GetMediaItemsRecursive(mr, _exportRoot, fixedNames)
                };

                var reportJson = Json.Encode(exportStructur);
                System.IO.File.WriteAllText(Path.Combine(_exportRoot, "export-report.json"), reportJson);

                if (fixedNames.Count > 0)
                {
                    reportJson = Json.Encode(fixedNames);
                    System.IO.File.WriteAllText(Path.Combine(_exportRoot, "export-fixednames.json"), reportJson);
                }

                _logger.Debug<ExportMedia>("Media Section exported.");
            }
            catch (Exception ex)
            {
                _logger.Error<ExportMedia>(ex);

                System.IO.File.WriteAllText(
                    Path.Combine(_exportRoot, "export-error.json"),
                    $"Media Section not exported! {System.Environment.NewLine}{ex.Message} {System.Environment.NewLine}{ex.Source} {System.Environment.NewLine}{ex.StackTrace}");
            }
        }

        private IEnumerable<MediaFolderAndFileInfo> GetMediaItemsRecursive(
            IEnumerable<IMedia> mr,
            string parentPath,
            List<FixedNames> fixedNames,
            MediaFolderAndFileInfo info = null)
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
                                _logger.Error<ExportMedia>(ex);
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
                    if (System.IO.File.Exists(umbracoFilePath))
                    {
                        if (!System.IO.File.Exists(exportPath))
                        {
                            System.IO.File.Copy(umbracoFilePath, exportPath);
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

                var childrenCount = _mediaService.CountChildren(item.ParentId);
                if (childrenCount > 0)
                {
                    long totalRecords = 1;
                    long pageIndex = 0;
                    var pageSize = 100;
                    var children = new List<IMedia>();
                    while (totalRecords > 0 && pageIndex < 5)
                    {
                        var childrenPage = _mediaService.GetPagedChildren(item.Id, pageIndex, pageSize, out totalRecords);
                        children.AddRange(childrenPage);
                        pageIndex++;
                    }

                    newExportElement.Children = GetMediaItemsRecursive(
                            children,
                            exportPath,
                            fixedNames,
                            newExportElement);
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
        #region helper classes
        public class ImageCropperValue
        {
            public string Src { get; set; }
            public FocalPoint FocalPoint { get; set; }
            public IEnumerable<Crop> Crops { get; set; }
        }
        public class Crop
        {
            public string Name { get; set; }
        }
        public class FocalPoint
        {
            public float Left { get; set; }
            public float Top { get; set; }
        }
        public class MediaFolderAndFileInfo
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
        public class FixedNames
        {
            public string UmbracoName { get; set; }
            public string FixedName { get; set; }
            public string ErrorMessage { get; set; }
        }
        #endregion
    }

    public class ExportMediaWhenAppStarted : IComponent
    {
        private readonly ILogger _logger;
        //private readonly UmbracoApplicationBase _umbracoApplication;
        private readonly IMediaService _mediaService;
        public ExportMediaWhenAppStarted(
            ILogger logger,
            //UmbracoApplicationBase umbracoApplication,
            IMediaService mediaService)
        {
            _logger = logger;
            //_umbracoApplication = umbracoApplication;
            _mediaService = mediaService;
        }

        public void Initialize()
        {
            var webRoot = "C:\\inetpub\\wwwroot\\umb\\v8.18.2";// _umbracoApplication.Server.MapPath("/");

            var exporter = new ExportMedia(_mediaService, webRoot, _logger);
        }

        public void Terminate()
        {

        }
    }

    [RuntimeLevel(MinLevel = RuntimeLevel.Run)]
    public class SubscribeToContentSavedEventComposer : ComponentComposer<ExportMediaWhenAppStarted> { }
}