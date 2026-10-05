mergeInto(LibraryManager.library, {
  QuotaMarkReady: function (messagePtr) {
    if (window.quotaReport) window.quotaReport(UTF8ToString(messagePtr));
  },

  QuotaEditName: function (valuePtr, targetPtr) {
    var target = UTF8ToString(targetPtr);
    var name = window.prompt("あなたの名前", UTF8ToString(valuePtr));
    if (name !== null && window.quotaUnityInstance) {
      window.quotaUnityInstance.SendMessage(target, "OnNameEdited", name);
    }
  },

  QuotaFetch: function (methodPtr, urlPtr, bodyPtr, clientPtr, targetPtr) {
    var method = UTF8ToString(methodPtr);
    var url = UTF8ToString(urlPtr);
    var body = UTF8ToString(bodyPtr);
    var client = UTF8ToString(clientPtr);
    var target = UTF8ToString(targetPtr);
    var options = {
      method: method,
      headers: {
        "Content-Type": "application/json",
        "X-Quota-Client": client
      },
      cache: "no-store"
    };
    if (method !== "GET") options.body = body || "{}";
    fetch(url, options)
      .then(function (response) {
        return response.text().then(function (text) {
          return {
            ok: response.ok,
            status: response.status,
            body: text,
            error: response.ok ? "" : response.statusText
          };
        });
      })
      .catch(function (error) {
        return {ok: false, status: 0, body: "", error: String(error)};
      })
      .then(function (result) {
        var instance = window.quotaUnityInstance;
        if (instance) instance.SendMessage(target, "OnNetworkResponse", JSON.stringify(result));
      });
  }
});
