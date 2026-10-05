mergeInto(LibraryManager.library, {
  QuotaMarkReady: function (messagePtr) {
    if (window.quotaReport) window.quotaReport(UTF8ToString(messagePtr));
  }
});
