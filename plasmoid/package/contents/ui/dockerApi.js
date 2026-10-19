.pragma library

// Shared HTTP helpers for talking to the Plasma Docker Monitor API, so the
// XMLHttpRequest/Result-envelope boilerplate has one home instead of being
// copy-pasted across main.qml, ContainerRow.qml and configGeneral.qml.

function getContainers(apiBaseUrl, onSuccess, onError) {
    const xhr = new XMLHttpRequest()
    xhr.onreadystatechange = function () {
        if (xhr.readyState !== XMLHttpRequest.DONE)
            return
        if (xhr.status !== 200) {
            onError("GET /containers failed with status " + xhr.status)
            return
        }
        try {
            const response = JSON.parse(xhr.responseText)
            if (!response.success) {
                onError("GET /containers returned failure: " + response.errorMessage)
                return
            }
            onSuccess(response.data)
        } catch (e) {
            onError("Failed to parse /containers response: " + e)
        }
    }
    xhr.open("GET", apiBaseUrl + "/containers")
    xhr.send()
}

function getContainerStats(apiBaseUrl, containerId, onSuccess, onError) {
    const xhr = new XMLHttpRequest()
    xhr.onreadystatechange = function () {
        if (xhr.readyState !== XMLHttpRequest.DONE)
            return
        if (xhr.status !== 200) {
            onError("GET stats for " + containerId + " failed with status " + xhr.status)
            return
        }
        try {
            const response = JSON.parse(xhr.responseText)
            if (!response.success) {
                onError("GET stats returned failure: " + response.errorMessage)
                return
            }
            onSuccess(response.data)
        } catch (e) {
            onError("Failed to parse stats response: " + e)
        }
    }
    xhr.open("GET", apiBaseUrl + "/containers/" + containerId + "/stats")
    xhr.send()
}

function performAction(apiBaseUrl, containerId, action, onDone) {
    const xhr = new XMLHttpRequest()
    xhr.onreadystatechange = function () {
        if (xhr.readyState !== XMLHttpRequest.DONE)
            return
        if (xhr.status !== 202)
            console.warn(action + " on " + containerId + " failed with status " + xhr.status)
        onDone()
    }
    xhr.open("POST", apiBaseUrl + "/containers/" + containerId + "/" + action)
    xhr.send()
}
