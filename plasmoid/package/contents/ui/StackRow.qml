pragma ComponentBehavior: Bound

import QtQuick
import QtQuick.Layouts
import org.kde.plasma.components as PlasmaComponents

Item {
    id: root

    property var stack: null
    property bool expanded: false
    property string scope: "top"
    property string itemId: ""
    property string apiBaseUrl: ""
    property var resolveShowCpu: null
    property var resolveShowMem: null

    signal actionRequested(string containerId, string action)
    signal moveRequested(string id, string scope, int direction)

    implicitWidth: columnLayout.implicitWidth
    implicitHeight: columnLayout.implicitHeight

    ColumnLayout {
        id: columnLayout
        anchors.left: parent.left
        anchors.right: parent.right
        spacing: 0

        RowLayout {
            Layout.fillWidth: true
            Layout.leftMargin: 8
            Layout.rightMargin: 8
            spacing: 8

            PlasmaComponents.ToolButton {
                icon.name: root.expanded ? "arrow-down" : "arrow-right"
                onClicked: root.expanded = !root.expanded
            }

            PlasmaComponents.Label {
                Layout.fillWidth: true
                text: root.stack ? root.stack.projectName : ""
                font.bold: true
                elide: Text.ElideRight
            }

            PlasmaComponents.Label {
                text: root.stack ? (root.stack.runningCount + "/" + root.stack.totalCount + " running") : ""
            }

            // Stack headers are ordered among their top-level siblings exactly
            // like standalone containers (spec §4.2) — they just don't get
            // CPU/Mem overrides themselves, only their children do.
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
        }

        ColumnLayout {
            Layout.fillWidth: true
            visible: root.expanded
            spacing: 0

            Repeater {
                model: root.stack ? root.stack.containers : []

                ContainerRow {
                    id: childRow

                    required property var modelData

                    Layout.fillWidth: true
                    container: childRow.modelData
                    indented: true
                    scope: root.stack ? root.stack.projectName : ""
                    itemId: childRow.modelData.id
                    apiBaseUrl: root.apiBaseUrl
                    resolveShowCpu: root.resolveShowCpu
                    resolveShowMem: root.resolveShowMem
                    onActionRequested: (containerId, action) => root.actionRequested(containerId, action)
                    onMoveRequested: (id, s, direction) => root.moveRequested(id, s, direction)
                }
            }
        }
    }
}
