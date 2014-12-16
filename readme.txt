Add an extra property to your (base) class to hold the content of your Blocks, mark it with the [AdditionalSearchContent] attribute.

Note the ScaffoldColumn(false) attribute, which hides it in edit mode.

        [CultureSpecific]
        [ScaffoldColumn(false)]
        [Searchable]
        [AdditionalSearchContent]
        public virtual string SearchText { get; set; }

All values from string properties marked [Searchable] in the Blocks will be added to the property marked with [AdditionalSearchContent].

Note: If for some reason you don't want a property on a Block indexed, just add [Seachable(false)] to it.

The content of the property marked with [AdditionalSearchContent] is added to the index and you get results also if the term you are looking for is only used in a Block on the page.
