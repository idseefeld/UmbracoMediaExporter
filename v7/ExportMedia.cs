using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.Helpers;
using Umbraco.Core;
using Umbraco.Core.Models;
using Umbraco.Core.Services;

namespace idseefeld.de
{
    /// <summary>
    /// ExportMedia exports all Umbraco media items into an external folder structur with item names for folders and files 
    /// as these are defined in Umbracos media section
    /// Put this file into [webroot]\App_Code folder.
    /// You might change the _exportRoot path to your needs. And you may set suitable permissions on that folder.
    /// and put the following line into your master or homepage template:
    /// @{ var mediaExporter = new idseefeld.de.ExportMedia(ApplicationContext); var mediaExporterResult = mediaExporter.Export(); }
    /// You may display @mediaExporterResult in your html, but that is not necassary.
    /// </summary>
    public class ExportMedia
    {
        private readonly string _exportRoot = "c:\\UmbracoMediaExport";
        //_exportRoot needs write permission for the applicationPool user.

        private readonly string _umbracoRoot;
        private readonly ApplicationContext _applicationContext;
        private readonly char[] _invalidFilenameChars;
        public ExportMedia(ApplicationContext applicationContext, Umbraco.Web.UmbracoContext umbracoContext)
        {
            _applicationContext = applicationContext;
            _invalidFilenameChars = Path.GetInvalidFileNameChars();
            _umbracoRoot = umbracoContext.HttpContext.Server.MapPath("/");
        }
        public ExportMedia(ApplicationContext applicationContext, string webRoot)
        {
            _applicationContext = applicationContext;
            _invalidFilenameChars = Path.GetInvalidFileNameChars();
            _umbracoRoot = webRoot;
            Export();
        }
        public string Export()
        {
            var rVal = "No Media Root found.";
            MediaFolderAndFileInfo exportStructur = null;
            try
            {
                var exportRoot = Directory.CreateDirectory(_exportRoot);
                if (exportRoot.EnumerateFiles().Count() > 0) return $"Media items allready exported. For new export delete all content of: {_exportRoot}";


                var ms = _applicationContext.Services.MediaService;
                var mr = ms.GetRootMedia();
                if (mr == null) return rVal;

                var fixedNames = new List<FixedNames>();
                exportStructur = new MediaFolderAndFileInfo()
                {
                    Name = "Media",
                    PathSegment = "",
                    Children = GetMediaItemsRecursive(mr, ms, _exportRoot, fixedNames)
                };

                var reportJson = Json.Encode(exportStructur);
                System.IO.File.WriteAllText(Path.Combine(_exportRoot, "export-report.json"), reportJson);

                if (fixedNames.Count > 0)
                {
                    reportJson = Json.Encode(fixedNames);
                    System.IO.File.WriteAllText(Path.Combine(_exportRoot, "export-fixednames.json"), reportJson);
                }

                rVal = "Media Section exported.";
            }
            catch (Exception ex)
            {
                rVal = "Media Section not exported!";
                System.IO.File.WriteAllText(
                    Path.Combine(_exportRoot, "export-error.json"),
                    $"{rVal} {System.Environment.NewLine}{ex.Message} {System.Environment.NewLine}{ex.Source} {System.Environment.NewLine}{ex.StackTrace}");
            }
            return rVal;
        }

        private IEnumerable<MediaFolderAndFileInfo> GetMediaItemsRecursive(
            IEnumerable<IMedia> mr,
            IMediaService ms,
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
                        .Value.ToString();
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
                            catch { }
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

                var children = item.Children();
                if (children != null)
                {
                    newExportElement.Children = GetMediaItemsRecursive(
                        children,
                        ms,
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

    /// <summary>
    /// The following ApplicationEventHandler adds automatic execution of ExportMedia.Export() on application start.
    /// Btw. the application starts when you change this file.
    /// </summary>
    public class ExportMediaWhenAppStarted : ApplicationEventHandler
    {
        protected override void ApplicationStarted(UmbracoApplicationBase umbracoApplication, ApplicationContext applicationContext)
        {
            var webRoot = umbracoApplication.Server.MapPath("/");
            var exporter = new ExportMedia(applicationContext, webRoot);
        }
    }
}