import Foundation
import PitboardKit

extension WindowAccount: Identifiable {
    /// The core derives the store as a UUID, so it always reads as one.
    public var id: UUID { UUID(uuidString: store)! }
}

extension OpenWindow: Identifiable {
    public var id: UUID { account.id }
}

extension PickerAccount: Identifiable {
    public var id: UUID { window.id }
}

extension DownloadShown: Identifiable {}
