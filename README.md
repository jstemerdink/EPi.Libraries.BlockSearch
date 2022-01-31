# Add block content to the index of EPiServer Search.
[![Build status](https://ci.appveyor.com/api/projects/status/3qrrg548g02j8eej/branch/master?svg=true)](https://ci.appveyor.com/project/jstemerdink/epi-libraries-blocksearch/branch/master)
[![GitHub version](https://badge.fury.io/gh/jstemerdink%2FEPi.Libraries.BlockSearch.svg)](http://badge.fury.io/gh/jstemerdink%2FEPi.Libraries.BlockSearch)
[![Platform](https://img.shields.io/badge/platform-.NET%205-blue.svg?style=flat)](https://msdn.microsoft.com/en-us/library/w0x726c2%28v=vs.110%29.aspx)
[![Platform](https://img.shields.io/badge/EPiServer-%2012.0.0-orange.svg?style=flat)](http://world.episerver.com/cms/)
[![GitHub license](https://img.shields.io/badge/license-MIT%20license-blue.svg?style=flat)](license.txt)
[![Quality Gate Status](https://sonarcloud.io/api/project_badges/measure?project=jstemerdink%3AEPi.Libraries.BlockSearch&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=jstemerdink%3AEPi.Libraries.BlockSearch)
[![NuGet](https://img.shields.io/badge/NuGet-Release-blue.svg)](http://nuget.episerver.com/en/OtherPages/Package/?packageId=EPi.Libraries.BlockSearch)
## About
Add Blocks used on a page to EPiServer Search Index as content of the page they were used on.
This is supported in Find, but not if you use EPiServer Search.
You need an extra property to your base class, or a specific pagetype to hold the content of the Blocks.
Note the ScaffoldColumn(false) attribute, which hides it in edit mode.
        [CultureSpecific]
        [ScaffoldColumn(false)]
        [Searchable]
        [AdditionalSearchContent]
        public virtual string SearchText { get; set; }
The aggregated content from string properties marked ```Searchable``` in the Blocks are added to the property marked with ```[AdditionalSearchContent]```.
Note: If for some reason you don't want a property on a Block indexed, just add ```[Seachable(false)]``` to it.
The content of the property marked with ```[AdditionalSearchContent]`` is added to the index and you get results also if the term you are looking for is only used in a Block on the page.




> *Powered by ReSharper*
> [![image](https://i0.wp.com/jstemerdink.files.wordpress.com/2017/08/logo_resharper.png)](http://jetbrains.com)
