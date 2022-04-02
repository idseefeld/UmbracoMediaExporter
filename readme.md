# Simple Media Exporter Solution for Umbraco
This is a simple App_Code based solution. The export is only done, when the target folder is empty. Nevertheless you should delete the *.cs file or change the its extension to i.e.: *.cs.excluded.

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

### ModelsBuilder Setup with Explicit Models
If you like to work with typed models in views or controllers you can configure _ModelsBuilder_ in web.config to generate your document types in ~/App_Code eg.:

```
<appSettings>
    ...
    <add key="Umbraco.ModelsBuilder.ModelsMode" value="AppData" />
	<add key="Umbraco.ModelsBuilder.ModelsDirectory" value="~/App_Code/Models" />
```
For this case you may use ```\v8\ModelsBuilderExplicitModels\ExportMediaV8.5.ModelsBuilder.cs``` 

## Umbraco 9
This will follow very soon.

So tune in again... 😉