import QtQuick
import QtQuick.Layouts
import org.kde.plasma.components as PlasmaComponents
import "dockerApi.js" as DockerApi

Item {
    id: root

    property var container: null
    property bool indented: false
    property string scope: "top"
    property string itemId: ""
    property string apiBaseUrl: ""

    // Function references passed down from main.qml (or forwarded through
    // StackRow), so every row resolves overrides the same way without each
    // one needing its own copy of the containerSettings parsing logic.
    property var resolveShowCpu: null
    property var resolveShowMem: null

    signal actionRequested(string containerId, string action)
    signal moveRequested(string id, string scope, int direction)

    readonly property bool effectiveShowCpu: resolveShowCpu && container ? resolveShowCpu(container.id) : false
    readonly property bool effectiveShowMem: resolveShowMem && container ? resolveShowMem(container.id) : false

    property var stats: null

    onEffectiveShowCpuChanged: updateStatsPolling()
    onEffectiveShowMemChanged: updateStatsPolling()
    onContainerChanged: updateStatsPolling()

    function updateStatsPolling() {
        statsTimer.running = root.container !== null && (root.effectiveShowCpu || root.effectiveShowMem)
    }

    function fetchStats() {
        if (!root.container || !root.apiBaseUrl)
            return
        DockerApi.getContainerStats(root.apiBaseUrl, root.container.id,
            function (data) { root.stats = data },
            function (message) { console.warn(message) })
    }

    Timer {
        id: statsTimer
        interval: 5000
        repeat: true
        triggeredOnStart: true
        onTriggered: root.fetchStats()
    }

    implicitWidth: rowLayout.implicitWidth
    implicitHeight: rowLayout.implicitHeight + 8

    function statusColor(state) {
        switch (state) {
            case "running":
                return "#2ecc71"
            case "starting":
            case "stopping":
            case "restarting":
                return "#f1c40f"
            case "exited":
            case "stopped":
                return "#e74c3c"
            default:
                return "#95a5a6"
        }
    }

    RowLayout {
        id: rowLayout
        anchors.verticalCenter: parent.verticalCenter
        anchors.left: parent.left
        anchors.right: parent.right
        anchors.leftMargin: root.indented ? 24 : 8
        anchors.rightMargin: 8
        spacing: 8

        Rectangle {
            implicitWidth: 10
            implicitHeight: 10
            radius: 5
            color: root.container ? root.statusColor(root.container.state) : "transparent"
        }

        PlasmaComponents.Label {
            Layout.fillWidth: true
            text: root.container ? root.container.name : ""
            elide: Text.ElideRight
        }

        PlasmaComponents.Label {
            visible: root.effectiveShowCpu && root.stats !== null
            text: root.stats ? root.stats.cpuPercent.toFixed(1) + "%" : ""
        }

        PlasmaComponents.Label {
            visible: root.effectiveShowMem && root.stats !== null
            text: root.stats ? root.stats.memUsedMb.toFixed(0) + " MB" : ""
        }

        // showLogsPreviewGlobal is global-only with no per-item override (spec
        // §4.1) and deferred until log-fetching exists — not wired up yet.

        // Arrows are always enabled, never disabled (spec §4.5) — there's no
        // non-manual sort mode to freeze out of yet, so there's nothing to
        // special-case here until Phase 4.
        PlasmaComponents.ToolButton {
            icon.name: "arrow-up"
            visible: root.itemId !== ""
            onClicked: root.moveRequested(root.itemId, root.scope, -1)
        }

        PlasmaComponents.ToolButton {
            icon.name: "arrow-down"
            visible: root.itemId !== ""
            onClicked: root.moveRequested(root.itemId, root.scope, 1)
        }

        PlasmaComponents.ToolButton {
            icon.name: "media-playback-start"
            visible: root.container !== null && root.container.state !== "running"
            onClicked: root.actionRequested(root.container.id, "start")
        }

        PlasmaComponents.ToolButton {
            icon.name: "media-playback-stop"
            visible: root.container !== null && root.container.state === "running"
            onClicked: root.actionRequested(root.container.id, "stop")
        }

        PlasmaComponents.ToolButton {
            icon.name: "view-refresh"
            visible: root.container !== null
            onClicked: root.actionRequested(root.container.id, "restart")
        }
    }
}
