mergeInto(LibraryManager.library, {
  QuotaNavigate: function (urlPtr) {
    window.location.assign(UTF8ToString(urlPtr));
  }
});
