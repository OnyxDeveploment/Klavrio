// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "KlavrioMac",
    platforms: [.macOS(.v13)],
    products: [
        .executable(name: "Klavrio", targets: ["KlavrioMacClient"])
    ],
    targets: [
        .executableTarget(
            name: "KlavrioMacClient",
            path: "Sources/KlavrioMacClient"
        ),
        .testTarget(
            name: "KlavrioMacClientTests",
            dependencies: ["KlavrioMacClient"],
            path: "Tests/KlavrioMacClientTests"
        )
    ]
)
