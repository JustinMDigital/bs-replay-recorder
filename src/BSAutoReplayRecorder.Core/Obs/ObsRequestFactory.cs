using System;
using System.Collections.Generic;
using BSAutoReplayRecorder.Core.Utility;

namespace BSAutoReplayRecorder.Core.Obs;

public static class ObsRequestFactory
{
    public static string CreateIdentifyRequest(int rpcVersion, string? authentication)
    {
        var auth = string.IsNullOrEmpty(authentication)
            ? ""
            : ",\"authentication\":\"" + JsonText.Escape(authentication!) + "\"";

        return "{\"op\":1,\"d\":{\"rpcVersion\":" + rpcVersion + auth + "}}";
    }

    public static string CreateRequest(string requestType, string requestId)
    {
        if (string.IsNullOrWhiteSpace(requestType))
        {
            throw new ArgumentException("OBS request type is required.", nameof(requestType));
        }

        if (string.IsNullOrWhiteSpace(requestId))
        {
            throw new ArgumentException("OBS request id is required.", nameof(requestId));
        }

        return "{\"op\":6,\"d\":{\"requestType\":\"" + JsonText.Escape(requestType) +
               "\",\"requestId\":\"" + JsonText.Escape(requestId) + "\"}}";
    }

    public static string CreateRequest(string requestType, string requestId, IReadOnlyDictionary<string, string> requestData)
    {
        if (requestData.Count == 0)
        {
            return CreateRequest(requestType, requestId);
        }

        var data = new List<string>();
        foreach (var item in requestData)
        {
            data.Add("\"" + JsonText.Escape(item.Key) + "\":\"" + JsonText.Escape(item.Value) + "\"");
        }

        return "{\"op\":6,\"d\":{\"requestType\":\"" + JsonText.Escape(requestType) +
               "\",\"requestId\":\"" + JsonText.Escape(requestId) +
               "\",\"requestData\":{" + string.Join(",", data) + "}}}";
    }
}

