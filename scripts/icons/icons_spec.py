"""Icon specification: which icon each menu action without an icon gets.

ICONS: key -> dict(
    m, f   : base glyph in the Material (m; also used by the colored Default set) / Fluent (f) family, or '@key' = an
             icon that already exists in the sets (shared by all three).
    mb, fb : optional badge glyph (small, bottom-right) in the Material / Fluent family.
)
ACTIONS: action id -> icon key (existing keys included).
"""

ICONS = {}


def icon(key, m=None, f=None, mb=None, fb=None, base=None):
    if base:
        m = f = base
    ICONS[key] = dict(m=m, f=f, mb=mb, fb=fb)


# ---- File -------------------------------------------------------------------------------------------------------
icon('file-new-html', m='html', f='document_code')
icon('file-new-css', m='css', f='document_css')
icon('file-new-js', m='javascript', f='document_javascript')
icon('file-new-svg', m='draw', f='shapes')
icon('document-close', m='close', f='dismiss')
icon('document-save-as', base='@document-save', mb='edit', fb='edit')
icon('document-save-copy', base='@document-save', mb='content_copy', fb='copy')
icon('revert-before', m='history', f='history', mb='arrow_back', fb='arrow_left')
icon('revert-after', m='history', f='history', mb='arrow_forward', fb='arrow_right')

# ---- Edit -------------------------------------------------------------------------------------------------------
icon('paste-history', base='@edit-paste', mb='history', fb='history')
icon('delete-line', m='playlist_remove', f='line_horizontal_3', fb='dismiss')
icon('toggle-comment', m='code', f='code', mb='comment', fb='comment')
icon('split-markers', base='@split-section', mb='flag', fb='flag')
icon('settings', m='settings', f='settings')
icon('merge-content', m='merge', f='merge')
icon('unwrap-div', m='close_fullscreen', f='arrow_minimize')
icon('rename-class', m='tag', f='tag', mb='edit', fb='edit')

# ---- Insert / Format --------------------------------------------------------------------------------------------
icon('section-marker', m='flag', f='flag')
icon('tag-closing', m='code', f='code', mb='add', fb='add')
icon('remove-formatting', m='format_clear', f='text_clear_formatting')
icon('tag-jump-opening', m='code', f='code', mb='arrow_back', fb='arrow_left')
icon('tag-jump-closing', m='code', f='code', mb='arrow_forward', fb='arrow_right')
icon('tag-select', m='code', f='code', mb='select_all', fb='select_all_on')
icon('tag-rename', m='code', f='code', mb='edit', fb='edit')
icon('tag-split', m='code', f='code', mb='call_split', fb='arrow_split')
icon('preserve-attributes', m='lock', f='lock_closed')

# ---- Search -----------------------------------------------------------------------------------------------------
icon('find-hide', m='search', f='search', mb='close', fb='dismiss')
icon('find-next', base='@edit-find', mb='arrow_downward', fb='arrow_down')
icon('find-previous', base='@edit-find', mb='arrow_upward', fb='arrow_up')
icon('replace-all-dry-run', base='@replace-all', mb='visibility', fb='eye')
icon('replace-all-filter', base='@replace-all', mb='filter_alt', fb='filter')
icon('go-to-definition', m='open_in_new', f='open')
icon('recent-locations', m='history', f='history')
icon('find-usages', m='manage_search', f='line_horizontal_4_search')
icon('mark-selection', m='highlight', f='highlight')
icon('go-to-line', m='format_list_numbered', f='text_number_list_ltr')

# ---- View / panels ----------------------------------------------------------------------------------------------
icon('word-wrap', m='wrap_text', f='text_wrap')
icon('panel-book-browser', m='folder_open', f='folder_open')
icon('panel-clips', m='text_snippet', f='notepad')
icon('panel-preview', m='preview', f='preview_link')
icon('panel-validation', m='fact_check', f='task_list_square_ltr')
icon('panel-notifications', m='notifications', f='alert')
icon('focus-book-browser', m='folder_open', f='folder_open', mb='my_location', fb='my_location')
icon('focus-code-editor', m='code', f='code', mb='my_location', fb='my_location')
icon('focus-preview', m='preview', f='preview_link', mb='my_location', fb='my_location')
icon('focus-toc', base='@edit-toc', mb='my_location', fb='my_location')
icon('focus-clips', m='text_snippet', f='notepad', mb='my_location', fb='my_location')

# ---- Window -----------------------------------------------------------------------------------------------------
icon('tab-next', m='tab', f='tab_desktop', mb='arrow_forward', fb='arrow_right')
icon('tab-previous', m='tab', f='tab_desktop', mb='arrow_back', fb='arrow_left')
icon('tab-close', m='tab', f='tab_desktop', mb='close', fb='dismiss')
icon('tab-close-others', m='tab', f='tab_desktop', mb='clear_all', fb='dismiss_square_multiple')
icon('file-previous', m='description', f='text_description', mb='arrow_back', fb='arrow_left')
icon('file-next', m='description', f='text_description', mb='arrow_forward', fb='arrow_right')

# ---- Tools ------------------------------------------------------------------------------------------------------
icon('add-cover', m='add_photo_alternate', f='image', fb='add')
icon('generate-toc-html', base='@generate-toc', mb='html', fb='code')
icon('standardize', m='auto_fix_high', f='wand')
icon('standard-extensions', m='description', f='text_description', mb='check', fb='checkmark')
icon('rebase-ids', m='tag', f='tag', mb='sync', fb='arrow_sync')
icon('custom-epub', m='book', f='book', mb='add', fb='add')
icon('manifest-media-types', m='description', f='text_description', mb='sync', fb='arrow_sync')
icon('manifest-properties', m='settings_suggest', f='document_settings')
icon('upgrade-epub3', m='upgrade', f='arrow_circle_up')
icon('ncx-generate', m='account_tree', f='text_bullet_list_tree', mb='add', fb='add')
icon('ncx-remove', m='account_tree', f='text_bullet_list_tree', mb='remove', fb='subtract')
icon('nav-add', m='menu_book', f='book_open', mb='add', fb='add')
icon('nav-add-nonlinear', m='menu_book', f='book_open', mb='block', fb='prohibited')
icon('nav-remove', m='menu_book', f='book_open', mb='remove', fb='subtract')
icon('check-wellformed', m='check_circle', f='checkmark_circle')
icon('validate-css', m='css', f='document_css', mb='check', fb='checkmark')
icon('reports', m='assessment', f='data_bar_vertical')
icon('clip-editor', m='edit_note', f='notepad_edit')
icon('insert-clip', m='playlist_add', f='clipboard_arrow_right')
icon('manage-fonts', m='font_download', f='text_font')
icon('footnotes-renumber', m='123', f='clipboard_number_123')
icon('footnotes-convert', m='swap_horiz', f='arrow_swap')
icon('footnotes-unify-letters', m='abc', f='text_proofing_tools_abc')
icon('hyphen-add', m='wrap_text', f='text_wrap', mb='add', fb='add')
icon('hyphen-remove', m='wrap_text', f='text_wrap', mb='remove', fb='subtract')
icon('spellcheck-highlight', m='abc', f='text_proofing_tools_abc', mb='highlight', fb='highlight')
icon('spellcheck-next', m='abc', f='text_proofing_tools_abc', mb='arrow_forward', fb='arrow_right')
icon('spellcheck-add', m='abc', f='text_proofing_tools_abc', mb='add', fb='add')
icon('spellcheck-ignore', m='abc', f='text_proofing_tools_abc', mb='visibility_off', fb='eye_off')
icon('spellcheck-clear-ignored', m='abc', f='text_proofing_tools_abc', mb='delete', fb='delete')

# ---- Saved Searches / Clip Editor list toolbar (plain RelayCommands, not catalog actions) -------------------------
icon('delete', m='delete', f='delete')
icon('add-entry', m='description', f='text_description', mb='add', fb='add')
icon('add-group', m='folder_open', f='folder_open', mb='add', fb='add')
icon('import', m='arrow_back', f='arrow_left')
icon('export', m='arrow_forward', f='arrow_right')
icon('fill-controls', m='content_copy', f='copy', mb='arrow_forward', fb='arrow_right')
icon('edit-search', m='edit', f='edit')

# ---- Book Browser (actions with AppActionIds constants: their keys are set in AppActionCatalog.cs by hand) ------------
icon('file-add-html', m='html', f='document_code', mb='add', fb='add')
icon('file-add-css', m='css', f='document_css', mb='add', fb='add')
icon('file-add-svg', m='draw', f='shapes', mb='add', fb='add')
icon('file-add-js', m='javascript', f='document_javascript', mb='add', fb='add')
icon('font-add', m='font_download', f='text_font', mb='add', fb='add')
icon('semantics-add', m='label', f='tag', mb='add', fb='add')
icon('rename', m='drive_file_rename_outline', f='rename')
icon('rename-template', m='drive_file_rename_outline', f='rename', mb='list', fb='list')
icon('rename-regex', m='drive_file_rename_outline', f='rename', mb='code', fb='code')
icon('move-to-folder', m='drive_file_move', f='folder_arrow_right')
icon('move-up', m='arrow_upward', f='arrow_up')
icon('move-down', m='arrow_downward', f='arrow_down')
icon('move-to-top', m='vertical_align_top', f='arrow_upload')
icon('move-to-bottom', m='vertical_align_bottom', f='arrow_download')
icon('sort', m='sort_by_alpha', f='text_sort_ascending')
icon('link-css', m='css', f='document_css', mb='link', fb='link')
icon('link-js', m='javascript', f='document_javascript', mb='link', fb='link')
icon('nav-mark', m='menu_book', f='book_open', mb='check', fb='checkmark')
icon('validate-w3c', m='public', f='globe', mb='check', fb='checkmark')
icon('file-copy', m='file_copy', f='document_copy')
icon('toc-renumber', m='format_list_numbered', f='text_number_list_ltr')
icon('file-info', m='info', f='info')

# ---- Toolbar context menu (plain commands, not catalog actions) -------------------------------------------------
icon('toolbar-configure', m='tune', f='options')
icon('lock', m='lock', f='lock_closed')

# ---- Action id -> icon key (existing keys are reused as they are) -----------------------------------------------
W = 'MainWindow.'
ACTIONS = {
    'NewHTMLFile': 'file-new-html', 'NewCSSFile': 'file-new-css', 'NewJSFile': 'file-new-js',
    'NewSVGFile': 'file-new-svg', 'Close': 'document-close', 'SaveAs': 'document-save-as',
    'SaveACopy': 'document-save-copy', 'RevertToBefore': 'revert-before', 'RevertToAfter': 'revert-after',
    'CreateCheckpoint': 'git-commit',
    'PasteClipboardHistory': 'paste-history', 'DeleteLine': 'delete-line', 'ToggleComment': 'toggle-comment',
    'SplitOnSGFSectionMarkers': 'split-markers', 'Preferences': 'settings', 'MergeContent': 'merge-content',
    'UnwrapDiv': 'unwrap-div', 'RenameClass': 'rename-class',
    'InsertSGFSectionMarker': 'section-marker', 'InsertClosingTag': 'tag-closing',
    'RemoveFormatting': 'remove-formatting', 'JumpToOpeningTag': 'tag-jump-opening',
    'JumpToClosingTag': 'tag-jump-closing', 'SelectTagContents': 'tag-select', 'RenameTag': 'tag-rename',
    'SplitTag': 'tag-split', 'HeadingPreserveAttributes': 'preserve-attributes',
    'HideFind': 'find-hide', 'FindNext': 'find-next', 'FindPrevious': 'find-previous',
    'ReplaceCurrent': 'replace', 'ReplaceNext': 'replace-find', 'ReplacePrevious': 'replace-find',
    'ReplaceAll': 'replace-all', 'Count': 'count-all', 'DryRunReplaceAll': 'replace-all-dry-run',
    'FilterReplaceAll': 'replace-all-filter', 'RestartSearch': 'restart',
    'FindNextInFile': 'find-next', 'ReplaceNextInFile': 'replace-find', 'ReplaceAllInFile': 'replace-all',
    'CountInFile': 'count-all', 'GoToLinkOrStyle': 'go-to-definition', 'NavigateBack': 'back',
    'NavigateForward': 'forward', 'RecentLocations': 'recent-locations', 'FindUsages': 'find-usages',
    'MarkSelection': 'mark-selection', 'GoToLine': 'go-to-line',
    'ZoomReset': 'zoom-to-window', 'WordWrap': 'word-wrap', 'BookBrowser': 'panel-book-browser',
    'ClipsWindow': 'panel-clips', 'PreviewWindow': 'panel-preview', 'TableOfContents': 'edit-toc',
    'ValidationResults': 'panel-validation', 'CheckpointsWindow': 'git-manage', 'FindReplaceWindow': 'find',
    'NotificationsWindow': 'panel-notifications', 'FindUsagesWindow': 'find-usages',
    'FocusOnBookBrowser': 'focus-book-browser', 'FocusOnCodeEditor': 'focus-code-editor',
    'FocusOnPreview': 'focus-preview', 'FocusOnTOC': 'focus-toc', 'FocusOnClips': 'focus-clips',
    'NextTab': 'tab-next', 'PreviousTab': 'tab-previous', 'CloseTab': 'tab-close',
    'CloseOtherTabs': 'tab-close-others', 'PreviousResource': 'file-previous', 'NextResource': 'file-next',
    'AddCover': 'add-cover', 'CreateHTMLTOC': 'generate-toc-html', 'StandardizeEpub': 'standardize',
    'UseStandardFileExtensions': 'standard-extensions', 'RebaseManifestIDs': 'rebase-ids',
    'CustomLayout': 'custom-epub', 'UpdateManifestMediaTypes': 'manifest-media-types',
    'UpdateManifestProperties': 'manifest-properties', 'UpgradeToEpub3': 'upgrade-epub3',
    'NCXGuideFromNav': 'ncx-generate', 'RemoveNCXGuide': 'ncx-remove', 'RemoveNavFromGuide': 'nav-remove',
    'AddNavToSpine': 'nav-add', 'AddNavToSpineNonLinear': 'nav-add-nonlinear',
    'WellFormedCheckEpub': 'check-wellformed', 'ValidateStylesheetsWithW3C': 'validate-css',
    'Reports': 'reports', 'ClipEditor': 'clip-editor', 'SelectClip': 'insert-clip', 'ManageFonts': 'manage-fonts',
    'LiveCssPanel': 'inspect', 'RenumberFootnotes': 'footnotes-renumber', 'ConvertFootnotes': 'footnotes-convert',
    'UnifyFootnoteLetters': 'footnotes-unify-letters', 'AddSoftHyphens': 'hyphen-add',
    'RemoveSoftHyphens': 'hyphen-remove', 'AutoSpellCheck': 'spellcheck-highlight',
    'Spellcheck': 'spellcheck-next', 'AddMispelledWord': 'spellcheck-add',
    'IgnoreMispelledWord': 'spellcheck-ignore', 'ClearIgnoredWords': 'spellcheck-clear-ignored',
}
ACTIONS = {W + k: v for k, v in ACTIONS.items()}
