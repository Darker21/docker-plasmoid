import QtQuick 2.0
import QtQuick.Layouts
import QtQuick.Controls 2.5 as QQC2
import org.kde.kirigami 2.4 as Kirigami
import org.kde.plasma.plasmoid
import "containerSettings.js" as ContainerSettings
import "dockerApi.js" as DockerApi

QQC2.ScrollView {
    id: page

    // Plasma's config loader scans this file's ROOT item for cfg_ properties
    // (confirmed against real plasmoid examples, e.g. desktopcontainment's
    // ConfigIcons.qml) — so these have to stay here on the ScrollView root
    // even though the controls they alias now live inside the nested
    // FormLayout below, not as direct children.

    // Simple settings bound straight to a control via the standard cfg_ alias
    // convention — these go through the normal Apply/OK/Cancel/Defaults flow.
    property alias cfg_apiBaseUrl: apiBaseUrlField.text
    property alias cfg_showCpuStatsGlobal: showCpuCheck.checked
    property alias cfg_showMemStatsGlobal: showMemCheck.checked
    property alias cfg_showLogsPreviewGlobal: showLogsCheck.checked

    // containerSettings isn't bound to any single control — it's a JSON blob
    // mutated by the arrow/override buttons below. Per KDE's own docs, that
    // means reading/writing Plasmoid.configuration directly (applies
    // immediately, bypassing Apply/OK) rather than a cfg_ alias, which is
    // documented specifically as a binding to a single control's property.

    property var standaloneContainers: []
    property var stacks: []
    property string statusText: ""

    function currentSettings() {
        return ContainerSettings.parseSettings(Plasmoid.configuration.containerSettings);
    }

    function refresh() {
        page.statusText = i18n("Loading…");
        DockerApi.getContainers(apiBaseUrlField.text, function (data) {
            page.standaloneContainers = data.standalone;
            page.stacks = data.stacks;
            page.statusText = "";

            const settings = page.currentSettings();
            const merged = ContainerSettings.mergeNewItems(settings, data.standalone, data.stacks);
            if (merged.length !== settings.length)
                Plasmoid.configuration.containerSettings = JSON.stringify(merged);
        }, function (message) {
            page.statusText = message;
            console.warn(message);
        });
    }

    Component.onCompleted: refresh()

    function moveItem(id, scope, direction) {
        const updated = ContainerSettings.moveItem(page.currentSettings(), id, scope, direction);
        Plasmoid.configuration.containerSettings = JSON.stringify(updated);
    }

    function overrideFor(id, kind) {
        const settings = page.currentSettings();
        const setting = settings.find(s => s.id === id);
        if (!setting)
            return null;
        return kind === "cpu" ? setting.cpuOverride : setting.memOverride;
    }

    function setOverride(id, kind, value) {
        const settings = page.currentSettings();
        const setting = settings.find(s => s.id === id);
        if (!setting)
            return;
        if (kind === "cpu")
            setting.cpuOverride = value;
        else
            setting.memOverride = value;
        Plasmoid.configuration.containerSettings = JSON.stringify(settings);
    }

    // Wrapping the actual visual content in its own FormLayout, inside the
    // ScrollView root, is what makes this page scroll instead of clipping —
    // a bare top-level FormLayout doesn't get scrolling for free.
    Kirigami.FormLayout {
        id: formLayout
        width: page.availableWidth

        QQC2.TextField {
            id: apiBaseUrlField
            Kirigami.FormData.label: i18n("API base URL:")
        }

        RowLayout {
            Kirigami.FormData.label: i18n("Containers:")

            QQC2.Button {
                text: i18n("Refresh")
                onClicked: page.refresh()
            }

            QQC2.Label {
                text: page.statusText
                opacity: 0.7
            }
        }

        Kirigami.Separator {
            Kirigami.FormData.isSection: true
            Kirigami.FormData.label: i18n("Global Defaults")
        }

        QQC2.CheckBox {
            id: showCpuCheck
            text: i18n("Show CPU stats")
        }

        QQC2.CheckBox {
            id: showMemCheck
            text: i18n("Show memory stats")
        }

        QQC2.CheckBox {
            id: showLogsCheck
            text: i18n("Show logs preview")
        }

        Kirigami.Separator {
            Kirigami.FormData.isSection: true
            Kirigami.FormData.label: i18n("Containers")
        }

        ColumnLayout {
            spacing: 4

            Repeater {
                model: ContainerSettings.getOrderedIds(page.currentSettings(), "top")

                delegate: Loader {
                    Layout.fillWidth: true
                    readonly property string rowItemId: modelData
                    readonly property var standaloneMatch: page.standaloneContainers.find(c => c.id === rowItemId)
                    readonly property var stackMatch: page.stacks.find(s => ("stack:" + s.projectName) === rowItemId)

                    sourceComponent: stackMatch ? stackDelegate : (standaloneMatch ? standaloneDelegate : null)

                    onLoaded: {
                        if (stackMatch)
                            item.stack = stackMatch;
                        else if (standaloneMatch)
                            item.container = standaloneMatch;
                    }
                }
            }
        }
    }

    Component {
        id: standaloneDelegate

        RowLayout {
            property var container: null

            QQC2.Label {
                Layout.fillWidth: true
                text: container ? container.name : ""
            }

            QQC2.ToolButton {
                icon.name: "arrow-up"
                onClicked: page.moveItem(container.id, "top", -1)
            }

            QQC2.ToolButton {
                icon.name: "arrow-down"
                onClicked: page.moveItem(container.id, "top", 1)
            }

            QQC2.ComboBox {
                model: [i18n("CPU: Default"), i18n("CPU: On"), i18n("CPU: Off")]
                currentIndex: {
                    const value = page.overrideFor(container.id, "cpu");
                    return value === null ? 0 : (value ? 1 : 2);
                }
                onActivated: index => page.setOverride(container.id, "cpu", index === 0 ? null : index === 1)
            }

            QQC2.ComboBox {
                model: [i18n("Mem: Default"), i18n("Mem: On"), i18n("Mem: Off")]
                currentIndex: {
                    const value = page.overrideFor(container.id, "mem");
                    return value === null ? 0 : (value ? 1 : 2);
                }
                onActivated: index => page.setOverride(container.id, "mem", index === 0 ? null : index === 1)
            }
        }
    }

    Component {
        id: stackDelegate

        ColumnLayout {
            property var stack: null
            spacing: 2

            RowLayout {
                Layout.fillWidth: true

                QQC2.Label {
                    Layout.fillWidth: true
                    font.bold: true
                    text: stack ? stack.projectName : ""
                }

                QQC2.ToolButton {
                    icon.name: "arrow-up"
                    onClicked: page.moveItem("stack:" + stack.projectName, "top", -1)
                }

                QQC2.ToolButton {
                    icon.name: "arrow-down"
                    onClicked: page.moveItem("stack:" + stack.projectName, "top", 1)
                }
            }

            Repeater {
                model: stack ? ContainerSettings.getOrderedIds(page.currentSettings(), stack.projectName) : []

                delegate: RowLayout {
                    Layout.fillWidth: true
                    Layout.leftMargin: 24
                    readonly property var childContainer: stack.containers.find(c => c.id === modelData)

                    QQC2.Label {
                        Layout.fillWidth: true
                        text: childContainer ? childContainer.name : ""
                    }

                    QQC2.ToolButton {
                        icon.name: "arrow-up"
                        onClicked: page.moveItem(childContainer.id, stack.projectName, -1)
                    }

                    QQC2.ToolButton {
                        icon.name: "arrow-down"
                        onClicked: page.moveItem(childContainer.id, stack.projectName, 1)
                    }

                    QQC2.ComboBox {
                        model: [i18n("CPU: Default"), i18n("CPU: On"), i18n("CPU: Off")]
                        currentIndex: {
                            const value = page.overrideFor(childContainer.id, "cpu");
                            return value === null ? 0 : (value ? 1 : 2);
                        }
                        onActivated: index => page.setOverride(childContainer.id, "cpu", index === 0 ? null : index === 1)
                    }

                    QQC2.ComboBox {
                        model: [i18n("Mem: Default"), i18n("Mem: On"), i18n("Mem: Off")]
                        currentIndex: {
                            const value = page.overrideFor(childContainer.id, "mem");
                            return value === null ? 0 : (value ? 1 : 2);
                        }
                        onActivated: index => page.setOverride(childContainer.id, "mem", index === 0 ? null : index === 1)
                    }
                }
            }
        }
    }
}
