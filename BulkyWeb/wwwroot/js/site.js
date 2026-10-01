// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

// Send the anti-forgery token (from the meta tag in _Layout) with every jQuery AJAX request,
// so DELETE/POST calls pass the server's CSRF check.
$.ajaxSetup({
    headers: { 'RequestVerificationToken': $('meta[name="csrf-token"]').attr('content') }
});
