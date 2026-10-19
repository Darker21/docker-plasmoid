pragma ComponentBehavior: Bound

import QtQuick
import QtQuick.Layouts
import QtQuick.Templates as T
import org.kde.plasma.components as PlasmaComponents
import org.kde.plasma.plasmoid
import "containerSettings.js" as ContainerSettings
import "dockerApi.js" as DockerApi

PlasmoidItem {
    id: root

    preferredRepresentation: fullRepresentation

    // Governs inline-panel placement — without this the containment is free
    // to squeeze the widget down and clip the row content (status dot, name,
    // CPU/Mem, action icons).
    Layout.minimumWidth: 320
    Layout.preferredWidth: 320

    readonly property string apiBaseUrl: Plasmoid.configuration.apiBaseUrl
    readonly property int pollIntervalMs: 5000

    readonly property bool showCpuStatsGlobal: Plasmoid.configuration.showCpuStatsGlobal
    readonly property bool showMemStatsGlobal: Plasmoid.configuration.showMemStatsGlobal
    readonly property bool showLogsPreviewGlobal: Plasmoid.configuration.showLogsPreviewGlobal

    // Mixed list of { type: "standalone", data: ContainerSummaryDto }
    // and { type: "stack", data: ComposeStackDto }, ordered per containerSettings.
    property var topLevelItems: []

    // Raw data from the last successful poll, kept so the ordering can be
    // rebuilt (e.g. after a manual reorder) without a fresh network round-trip.
    property var lastStandalone: []
    property var lastStacks: []

    function readContainerSettings() {
        return ContainerSettings.parseSettings(Plasmoid.configuration.containerSettings);
    }

    function writeContainerSettings(settings) {
        Plasmoid.configuration.containerSettings = JSON.stringify(settings);
    }

    function resolveShowCpu(containerId) {
        return ContainerSettings.resolveShowCpu(readContainerSettings(), containerId, root.showCpuStatsGlobal);
    }

    function resolveShowMem(containerId) {
        return ContainerSettings.resolveShowMem(readContainerSettings(), containerId, root.showMemStatsGlobal);
    }

    function moveItem(itemId, scope, direction) {
        const updated = ContainerSettings.moveItem(readContainerSettings(), itemId, scope, direction);
        writeContainerSettings(updated);
        rebuildTopLevelItems();
    }

    function rebuildTopLevelItems() {
        buildTopLevelItems(root.lastStandalone, root.lastStacks);
    }

    function buildTopLevelItems(standalone, stacks) {
        root.lastStandalone = standalone;
        root.lastStacks = stacks;

        const settings = readContainerSettings();
        const merged = ContainerSettings.mergeNewItems(settings, standalone, stacks);
        if (merged.length !== settings.length)
            writeContainerSettings(merged);

        const standaloneById = {};
        for (const c of standalone)
            standaloneById[c.id] = {
                type: "standalone",
                data: c
            };

        const stacksById = {};
        for (const s of stacks)
            stacksById["stack:" + s.projectName] = {
                type: "stack",
                data: s
            };

        const orderedIds = ContainerSettings.getOrderedIds(merged, "top");
        const items = [];
        for (const id of orderedIds) {
            if (standaloneById[id])
                items.push(standaloneById[id]);
            else if (stacksById[id])
                items.push(stacksById[id]);
        }
        root.topLevelItems = items;
    }

    function refreshContainers() {
        DockerApi.getContainers(root.apiBaseUrl, function (data) {
            root.buildTopLevelItems(data.standalone, data.stacks);
        }, function (message) {
            console.warn(message);
        });
    }

    function performAction(containerId, action) {
        DockerApi.performAction(root.apiBaseUrl, containerId, action, function () {
            root.refreshContainers();
        });
    }

    Timer {
        interval: root.pollIntervalMs
        running: true
        repeat: true
        triggeredOnStart: true
        onTriggered: root.refreshContainers()
    }

    fullRepresentation: ListView {
        id: containerListView
        implicitWidth: 320
        implicitHeight: 240
        Layout.minimumWidth: 320
        Layout.minimumHeight: 120
        clip: true
        model: root.topLevelItems

        T.ScrollBar.vertical: PlasmaComponents.ScrollBar {}

        delegate: Loader {
            id: rowLoader

            required property var modelData

            width: containerListView.width
            source: modelData.type === "stack" ? "StackRow.qml" : "ContainerRow.qml"

            onLoaded: {
                item.apiBaseUrl = root.apiBaseUrl;
                item.resolveShowCpu = root.resolveShowCpu;
                item.resolveShowMem = root.resolveShowMem;
                item.scope = "top";

                if (rowLoader.modelData.type === "stack") {
                    item.stack = rowLoader.modelData.data;
                    item.itemId = "stack:" + rowLoader.modelData.data.projectName;
                } else {
                    item.container = rowLoader.modelData.data;
                    item.itemId = rowLoader.modelData.data.id;
                }

                // Loader.item is dynamically typed (it switches between
                // ContainerRow and StackRow based on `source`), so qmllint
                // can't statically verify actionRequested/moveRequested
                // exist on it — known, accepted limitation of this pattern.
                item.actionRequested.connect(root.performAction);
                item.moveRequested.connect(root.moveItem);
            }
        }
    }
}
