// Shared TinyMCE setup for the admin Upsert pages (Product, Company).
// Only free plugins are listed: the premium ones came from a trial that ended on 2025-12-27,
// and every premium plugin requested without a paid key shows a warning in the editor.
tinymce.init({
    selector: 'textarea',
    plugins: [
        'anchor', 'autolink', 'charmap', 'codesample', 'emoticons', 'link', 'lists',
        'media', 'searchreplace', 'table', 'visualblocks', 'wordcount'
    ],
    toolbar: 'undo redo | blocks fontfamily fontsize | bold italic underline strikethrough | ' +
        'link media table | align lineheight | numlist bullist indent outdent | emoticons charmap | removeformat'
});
