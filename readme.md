# Simple Media Exporter Solution for Umbraco 7
## Umbraco 7
Drop file v7\ExportMedia.cs into your Umbraco webroot folder in App_Code (you may need to create this folder).

Edit line 22 of ExportMedia.cs: 
```
private readonly string _exportRoot = "c:\\UmbracoMediaExport";
//the applicationPool user needs write permission for _exportRoot folder.
```
Currently this solution is tested with Umbraco version 7.14.1 and 7.15.7.

## Umbraco 8
Drop file v8\ExportMediaV8.cs into your Umbraco webroot folder in App_Code (you may need to create this folder).

Edit line 24 of ExportMediaV8.cs: 
```
private readonly string _exportRoot = "c:\\UmbracoMediaExport";
//the applicationPool user needs write permission for _exportRoot folder.
```
Currently this solution is tested with Umbraco version 8.18.2.