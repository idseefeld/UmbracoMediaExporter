# Simple Media Exporter Solution for Umbraco 7
Drop file ExportMedia.cs into your Umbraco webroot folder in App_Code (you may need to create this folder).

Edit line 22 of ExportMedia.cs: 
```
private readonly string _exportRoot = "c:\\UmbracoMediaExport";
//the applicationPool user needs write permission for _exportRoot folder.
```
Currently this solution is tested with Umbraco version 7.14.1 and 7.15.7.
