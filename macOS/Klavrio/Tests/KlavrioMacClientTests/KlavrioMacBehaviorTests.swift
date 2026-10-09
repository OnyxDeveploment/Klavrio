import XCTest
@testable import KlavrioMacClient

final class KlavrioMacBehaviorTests: XCTestCase {
    private var temporary: URL!
    private var engine: LayoutCorrectionPipeline!
    private var converter: SelectedTextTransformer!

    override func setUpWithError() throws {
        temporary = FileManager.default.temporaryDirectory.appendingPathComponent("KlavrioMacClientTests-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: temporary, withIntermediateDirectories: true)
        try "hello\nunknownword\n".write(to: temporary.appendingPathComponent("english.txt"), atomically: true, encoding: .utf8)
        try "שלום\nמה\nאתה\nקורא\nקורה\nאנגלית\nבמשימה\n".write(to: temporary.appendingPathComponent("hebrew.txt"), atomically: true, encoding: .utf8)
        try "سلام\n".write(to: temporary.appendingPathComponent("arabic.txt"), atomically: true, encoding: .utf8)
        try "привет\n".write(to: temporary.appendingPathComponent("russian.txt"), atomically: true, encoding: .utf8)
        try "zeit\n".write(to: temporary.appendingPathComponent("german.txt"), atomically: true, encoding: .utf8)
        try "avec\n".write(to: temporary.appendingPathComponent("french.txt"), atomically: true, encoding: .utf8)
        try "typed_on_english_keyboard\tcorrect_hebrew\nnv\tמה\nt,v\tאתה\neurt\tקורא\ncnahnv\tבמשימה\n".write(to: temporary.appendingPathComponent("hebrew_key_pairs.tsv"), atomically: true, encoding: .utf8)
        try "latin_spelling\thebrew_word\nanglit\tאנגלית\n".write(to: temporary.appendingPathComponent("phonetic_manual.tsv"), atomically: true, encoding: .utf8)
        try "hebrew_word\tpriority\nבמשימה\t10\n".write(to: temporary.appendingPathComponent("hebrew_priority.tsv"), atomically: true, encoding: .utf8)
        engine = try LayoutCorrectionPipeline(directories: [temporary])
        converter = SelectedTextTransformer(engine: engine)
    }

    override func tearDownWithError() throws { try? FileManager.default.removeItem(at: temporary) }

    func testKeyboardMapsMatchWindowsVersion() throws {
        try KeyboardLayoutCatalog.validateMaps()
        XCTAssertEqual(KeyboardLayoutCatalog.convert("akuo", from: .english, to: .hebrew), "שלום")
        XCTAssertEqual(engine.translate("שלום"), "akuo")
        XCTAssertEqual(KeyboardLayoutCatalog.convert("sghl", from: .english, to: .arabic), "سلام")
        XCTAssertEqual(KeyboardLayoutCatalog.convert("ghbdtn", from: .english, to: .russian), "привет")
        XCTAssertEqual(KeyboardLayoutCatalog.convert("yeit", from: .english, to: .german), "zeit")
        XCTAssertEqual(KeyboardLayoutCatalog.convert("qvec", from: .english, to: .french), "avec")
    }

    func testReviewedAndMixedConversions() {
        XCTAssertEqual(engine.suggest("eurt"), "קורא")
        XCTAssertEqual(engine.suggest("nv"), "מה")
        XCTAssertEqual(engine.suggest("t,v"), "אתה")
        XCTAssertEqual(converter.convert("nv t,v eurt", mode: .auto), "מה אתה קורא")
        XCTAssertEqual(converter.convert("VHH ma kore", mode: .auto), "היי מה קורה")
        XCTAssertEqual(converter.convert("anglit", mode: .auto), "אנגלית")
        XCTAssertNil(engine.suggest("cnahnv"))
        XCTAssertEqual(converter.convert("cnahnv", mode: .auto), "במשימה")
    }

    func testExplicitAndMixedSelectionModes() {
        XCTAssertEqual(converter.convert("nv eurv", mode: .toHebrew), "מה קורה")
        XCTAssertEqual(converter.convert("ma kore hello", mode: .auto), "מה קורה hello")
        XCTAssertEqual(converter.convert("מה eurt", mode: .toHebrew), "מה קורא")
        XCTAssertEqual(converter.convert("sghl", mode: .toArabic), "سلام")
        XCTAssertEqual(converter.convert("ghbdtn", mode: .toRussian), "привет")
        XCTAssertEqual(converter.convert("yeit", mode: .toGerman), "zeit")
        XCTAssertEqual(converter.convert("qvec", mode: .toFrench), "avec")
    }

    func testKnownEnglishIsPreserved() {
        XCTAssertEqual(converter.convert("hello", mode: .auto), "hello")
        XCTAssertEqual(converter.convert("unknownword", mode: .auto), "unknownword")
    }

    func testDictionaryReload() throws {
        let pairs = temporary.appendingPathComponent("hebrew_key_pairs.tsv")
        let handle = try FileHandle(forWritingTo: pairs)
        try handle.seekToEnd()
        try handle.write(contentsOf: Data("zzw\tמחדש\n".utf8))
        try handle.close()
        try engine.reload()
        XCTAssertEqual(engine.exactHebrew("zzw"), "מחדש")
    }
}
