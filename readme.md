# Simple Media Exporter Solution for Umbraco 7
Drop file ExportMedia.cs into your Umbraco webroot folder in App_Code (you may need to create this folder).
Edit line 22 ExportMedia.cs and set 
```
private readonly string _exportRoot = "c:\\UmbracoMediaExport";
//the applicationPool user needs write permission for _exportRoot folder.
```

Copy the following line of code into a Umbraco template eg. Master:
```
@{ var mediaExporter = new ExportMedia(ApplicationContext); var mediaExporterResult = mediaExporter.Export(); }
```
You may display the _mediaExporterResult_ variable with @mediaExporterResult, but that is not required.

Currently this solution is tested with Umbraco version 7.14.1
